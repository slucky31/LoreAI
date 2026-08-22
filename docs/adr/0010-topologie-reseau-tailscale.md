# 0010 — Topologie réseau : accès privé par Tailscale

## Statut
Acceptée — précise et corrige la décision **D2** de la [roadmap](../roadmap.md) (« le serveur MCP reste strictement en LAN »).

## Contexte

La roadmap posait comme décision actée que le futur serveur MCP resterait **strictement en LAN** : *« jamais exposé sur Internet, pas de tunnel, pas d'accès nomade »*. Le lot 3 en découlait directement, avec une configuration client visant `http://raspberrypi.local:5099/mcp`.

Cette décision reposait sur une hypothèse implicite jamais vérifiée : **que le poste de travail se trouve sur le même réseau que le Raspberry Pi.** Ce n'est pas le cas. Le développement se fait depuis un **Shadow PC** — une machine Windows hébergée dans le cloud (`CsManufacturer: Blade`, `CsModel: Shadow Computer`). Elle n'est physiquement sur aucun LAN domestique, et `raspberrypi.local` n'y résout rien.

Prise au pied de la lettre, D2 rendait donc le serveur MCP **inaccessible depuis la seule machine censée l'utiliser**. Le lot 3 aurait été livré inutilisable.

Le constat qui débloque : **Tailscale est déjà installé et actif sur le poste de travail**, et le Pi figure déjà comme nœud du tailnet. L'infrastructure nécessaire existe, elle n'avait simplement pas été prise en compte lors de la rédaction de D2.

Il faut distinguer deux choses que la formulation d'origine confondait :

- **L'intention de D2** — ne jamais exposer le service sur l'Internet public, aucune redirection de port sur la box. Cette intention est bonne et n'est pas remise en cause.
- **Sa formulation** — « en LAN », « pas de tunnel ». Trop étroite : elle interdit le mécanisme même qui permet de tenir l'intention depuis une machine distante.

## Décision

Le périmètre d'accès est un **réseau privé**, entendu comme **LAN _ou_ tailnet**, jamais l'Internet public.

1. **Aucune exposition publique.** Pas de redirection de port sur la box, pas d'enregistrement DNS public, pas de reverse proxy exposé. **Tailscale Funnel est explicitement proscrit** sur ces services : c'est précisément le mécanisme qui publierait le service sur Internet.
2. **Les services privés écoutent sur l'interface Tailscale**, et sont adressés par leur nom MagicDNS plutôt que par `raspberrypi.local` ou une IP de LAN — le nom MagicDNS fonctionne depuis n'importe quel nœud du tailnet, à la maison comme depuis le Shadow.
3. **Le tailnet n'est pas considéré comme une frontière de sécurité suffisante à lui seul.** Le token bearer du serveur MCP reste obligatoire, et le rôle PostgreSQL en lecture seule (`loreai_ro`, cf. [ADR 0009](0009-postgresql-mutualise-sur-le-pi.md)) reste la garantie de non-écriture. Défense en profondeur : le réseau limite qui peut frapper à la porte, il ne remplace pas la serrure.
4. **Les ACL Tailscale restreignent les accès au strict nécessaire** — quel nœud peut atteindre quel port — plutôt que d'ouvrir tout à tous les nœuds du tailnet.
5. **Le Pi (`mcm8`, soit `mcm8.piranha-pollux.ts.net`) doit être un nœud en ligne du tailnet.** Ce n'est pas un détail de configuration : c'est un **prérequis d'exploitation**.
6. **L'expiration de clé doit être désactivée sur les nœuds serveurs.** Par défaut, Tailscale fait expirer la clé d'un nœud au bout de plusieurs mois : le nœud quitte alors le tailnet **sans panne, sans message et sans que la machine ne s'arrête**. Pour un serveur toujours allumé, c'est une bombe à retardement silencieuse.

   Ce point n'est pas théorique. Au moment d'écrire cet ADR, `mcm8` et `proxy` étaient hors du tailnet depuis 17 jours, et `tailscale ping` répondait `peer's node key has expired`. Les deux se sont tus à **5 min 36 s d'intervalle** — un écart incompatible avec une coupure de courant, mais exactement ce que produisent deux minuteries d'expiration lancées lors d'une même session d'installation. Les machines n'étaient jamais tombées ; elles étaient devenues invisibles.

## Alternatives écartées

- **Rester strictement sur le LAN.** Imposerait de ne développer et de n'interroger le corpus que depuis une machine physiquement à la maison. Incompatible avec le poste de travail réel. C'est la contradiction que cet ADR résout.
- **Redirection de port + reverse proxy + TLS.** Techniquement faisable, mais c'est exactement ce que l'intention de D2 refuse : le service devient joignable depuis l'Internet public. Ajoute en prime la gestion de certificats et le durcissement d'une surface exposée, pour un outil mono-utilisateur.
- **Cloudflare Tunnel.** Évite la redirection de port, mais publie tout de même le service sur Internet derrière une authentification tierce, et insère un tiers dans le **chemin de données**. Tailscale, lui, ne voit passer que la coordination de clés — le trafic reste chiffré de bout en bout entre les nœuds.
- **WireGuard ou OpenVPN configurés à la main.** Même résultat fonctionnel, mais toute la distribution et la rotation des clés reste à faire manuellement. Tailscale, c'est WireGuard avec ce problème déjà résolu.
- **ZeroTier.** Équivalent sur le fond. Écarté uniquement parce que Tailscale est déjà installé, déjà en service et déjà maîtrisé — introduire un second overlay serait gratuit.

## Conséquences

- **D2 est reformulée** : « réseau privé strict — LAN ou tailnet, jamais d'exposition publique ». L'intention est préservée, la formulation cesse d'être fausse pour le poste de travail réel.
- **Le lot 3 vise le nom MagicDNS du Pi**, pas `raspberrypi.local`. Le `docker-compose.yml` publie le port du serveur MCP sur l'interface Tailscale plutôt que sur toutes les interfaces.
- **Bénéfice collatéral immédiat** : l'instance PostgreSQL mutualisée devient elle aussi joignable depuis les postes de travail. Les tests, eux, n'en ont pas besoin — ils tournent sur une base jetable via Testcontainers ([ADR 0009](0009-postgresql-mutualise-sur-le-pi.md)) — mais cela permet d'inspecter le corpus réel et de rejouer un cas de production à la main.
- **Nouveau mode de panne** : si le Pi quitte le tailnet ou reste hors ligne, le MCP et la base de dev deviennent injoignables. À traiter comme un prérequis d'exploitation, au même titre que la disponibilité de la base (ADR 0009).
- **Dépendance à un tiers pour le plan de contrôle.** Tailscale coordonne les clés ; une panne de son service empêche l'établissement de nouvelles connexions. Le trafic lui-même ne transite pas par ses serveurs. Acceptable pour un usage personnel, mais c'est une dépendance de plus, et elle n'existait pas avant.
- **Aucun impact sur les tests ni sur la CI**, qui ne dépendent d'aucun accès au tailnet.
- **La décision ne dépend pas d'une machine en particulier.** Le développement peut se faire depuis plusieurs postes — le Shadow, ou un environnement doté de Docker pour exécuter la suite de tests ([ADR 0009](0009-postgresql-mutualise-sur-le-pi.md)). Chacun doit être un nœud du tailnet ; le raisonnement vaut pour tout poste hors du LAN, et n'est pas invalidé si l'un d'eux s'y trouve.
