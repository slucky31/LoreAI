using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class ClassificationNoteBuilderTests
{
    [Fact]
    public void Build_WithoutExistingNote_ReturnsBlockAlone()
    {
        var note = ClassificationNoteBuilder.Build(null, CreateClassification("Outil à essayer."));

        Assert.Equal("[LoreAI] ATester — Haute — Outil à essayer.", note);
    }

    [Fact]
    public void Build_PreservesUserNote()
    {
        var note = ClassificationNoteBuilder.Build("Ma note perso.", CreateClassification("Outil à essayer."));

        Assert.Equal("Ma note perso.\n\n[LoreAI] ATester — Haute — Outil à essayer.", note);
    }

    /// <summary>Le cas qui a motivé F-04 : la note est relue depuis l'API à chaque cycle.</summary>
    [Fact]
    public void Build_AppliedTwice_IsIdempotent()
    {
        var classification = CreateClassification("Outil à essayer.");

        var first = ClassificationNoteBuilder.Build("Ma note perso.", classification);
        var second = ClassificationNoteBuilder.Build(first, classification);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_ReplacesPreviousBlockInsteadOfStacking()
    {
        var previous = ClassificationNoteBuilder.Build("Ma note perso.", CreateClassification("Première analyse."));

        var updated = ClassificationNoteBuilder.Build(previous, CreateClassification("Analyse revue."));

        Assert.Equal("Ma note perso.\n\n[LoreAI] ATester — Haute — Analyse revue.", updated);
        Assert.DoesNotContain("Première analyse.", updated);
    }

    [Fact]
    public void Build_CleansUpBlocksAccumulatedBeforeTheFix()
    {
        var polluted = "Ma note perso.\n\n[LoreAI] ALire — Basse — un\n\n[LoreAI] ALire — Basse — deux\n\n[LoreAI] ALire — Basse — trois";

        var updated = ClassificationNoteBuilder.Build(polluted, CreateClassification("Analyse à jour."));

        Assert.Equal("Ma note perso.\n\n[LoreAI] ATester — Haute — Analyse à jour.", updated);
    }

    [Fact]
    public void Build_KeepsUserTextWrittenBelowTheBlock()
    {
        var existing = "[LoreAI] ALire — Basse — ancienne analyse\n\nRemarque ajoutée à la main.";

        var updated = ClassificationNoteBuilder.Build(existing, CreateClassification("Analyse à jour."));

        Assert.Equal("Remarque ajoutée à la main.\n\n[LoreAI] ATester — Haute — Analyse à jour.", updated);
    }

    /// <summary>
    /// Un bloc multi-ligne serait irrécupérable au passage suivant : la justification est aplatie
    /// pour que le bloc reste toujours identifiable sur une seule ligne.
    /// </summary>
    [Fact]
    public void Build_CollapsesMultilineReasonToASingleLine()
    {
        var classification = CreateClassification("Raison sur\ndeux lignes.");

        var note = ClassificationNoteBuilder.Build(null, classification);

        Assert.Equal("[LoreAI] ATester — Haute — Raison sur deux lignes.", note);
        Assert.Equal(note, ClassificationNoteBuilder.Build(note, classification));
    }

    [Fact]
    public void Build_WhitespaceOnlyNote_IsTreatedAsEmpty()
    {
        var note = ClassificationNoteBuilder.Build("   \n  ", CreateClassification("Outil à essayer."));

        Assert.Equal("[LoreAI] ATester — Haute — Outil à essayer.", note);
    }

    private static ClassificationResult CreateClassification(string reason) =>
        new(null, [], RecommendedAction.ATester, Priority.Haute, reason, "model", "raw");
}
