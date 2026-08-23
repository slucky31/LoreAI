namespace LoreAI.Core.Enums;

/// <summary>
/// D'où provient un <see cref="Models.LibraryItem"/> au moment de l'indexation : encore dans « Non trié »
/// (pas encore traité par <c>UnsortedClassificationJob</c>) ou déjà rangé par l'utilisateur (lot 1, #42).
/// </summary>
public enum ItemOrigin
{
    Unsorted,
    Library,
}
