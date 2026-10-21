# Orchestra Rave

**Projet Unity XR — Hand Tracking — Expérience musicale interactive**

## 1. Présentation du projet

**Orchestra Rave** est une expérience interactive en réalité virtuelle ou mixte dans laquelle l'utilisateur incarne un chef d'orchestre capable de diriger et de transformer une composition musicale grâce aux mouvements de ses mains.

L'expérience commence dans une salle de concert historique, inspirée de l'esthétique baroque et rococo. Face au joueur se trouve un orchestre traditionnel, composé de plusieurs familles instrumentales.

À travers ses gestes, le joueur peut modifier le volume général, appliquer des effets sonores, sélectionner des familles d'instruments et contrôler leur participation à la musique.

Progressivement, l'orchestre classique évolue vers une ambiance électronique inspirée de l'univers musical de Perceval, mêlant sonorités traditionnelles et techno. Le décor accompagne cette transformation : apparition d'enceintes, éclairages colorés et effets visuels propres aux rave parties.

### Objectifs

- Expérimenter le hand tracking dans Unity.
- Concevoir une interaction musicale intuitive sans contrôleurs.
- Manipuler une composition orchestrale grâce aux mouvements des mains.
- Permettre un contrôle global et individuel des familles instrumentales.
- Fusionner musique classique et musique électronique.
- Faire évoluer l'environnement visuel en fonction de la musique et de ses transformations.

### Type d'expérience

Le projet est un **sandbox musical**.

Il ne comprend ni objectifs de jeu, ni système de score, ni progression imposée. L'utilisateur est libre d'expérimenter avec les instruments et les effets.

L'interface utilisateur est volontairement absente ou réduite au strict minimum. Les retours d'interaction sont intégrés directement à l'environnement.

---

## 2. Périmètre du MVP

Le projet est développé par **4 personnes sur 2 jours**.

Cette première version se concentre sur un nombre restreint d'interactions, afin d'obtenir rapidement un prototype fonctionnel et stable.

### Familles instrumentales

L'orchestre est simplifié en cinq groupes :

| Groupe | Instruments représentés |
|---|---|
| Cordes | Violons, altos, violoncelles, contrebasses |
| Bois | Flûtes, clarinettes, hautbois, bassons |
| Cuivres | Trompettes, cors, trombones |
| Percussions | Timbales et autres percussions |
| Electro | Basses et autres electro (invisible au départ) |

Chaque famille constitue une unité contrôlable indépendamment.

### Commandes gestuelles

| Contrôle | Geste | Fonction |
|---|---|---|
| Main gauche | Déplacement vertical | Volume global |
| Main gauche | Rotation du poignet | Filtre audio global |
| Main droite | Pointage vers une famille | Sélection de la famille |
| Main droite | Pincement pouce-index | Mute / Unmute de la famille |
| Main droite | Poing fermé et déplacement vertical | Volume individuel de la famille |

La main gauche contrôle les paramètres globaux de l'orchestre.

La main droite permet de sélectionner et de modifier une famille instrumentale indépendamment.

Une baguette de chef d'orchestre manipulable est envisagée comme extension, mais ne fait pas partie des fonctionnalités prioritaires du MVP.

---

## 3. Les trois phases de l'expérience

Le projet se déroule dans une scène Unity unique comportant trois états musicaux et visuels.

### Phase 1 — Classical

L'utilisateur se trouve devant un orchestre traditionnel dans une salle historique.

**Musique :**
- Composition orchestrale acoustique.
- Familles instrumentales indépendantes.
- Contrôle du volume et du filtre.
- Activation et désactivation des familles.

**Environnement :**
- Salle de concert baroque / rococo.
- Musiciens en costumes historiques.
- Éclairage chaud.
- Absence d'éléments électroniques visibles.

### Phase 2 — Transition

La composition commence à intégrer des sonorités électroniques.

**Musique :**
- Introduction progressive de pistes électroniques.
- Mélange entre instruments traditionnels et électroniques.
- Maintien des contrôles gestuels.

**Environnement :**
- Apparition progressive d'éléments techno.
- Premiers changements d'éclairage.
- Introduction des couleurs et effets lumineux.

### Phase 3 — Techno / Classical

L'orchestre évolue vers une véritable performance hybride.

**Musique :**
- Mélange orchestral et techno.
- Présence renforcée des basses et rythmes électroniques.
- Conservation des commandes de manipulation sonore.

**Environnement :**
- Apparition de grands caissons de basses.
- Éclairages dynamiques.
- Effets visuels de rave party.
- Contraste entre décor historique et ambiance électronique.

Les trois phases ne nécessitent pas de changement de scène Unity.

Leur déclenchement est centralisé par le système Interaction. Pour le MVP, un contrôle de test peut être utilisé ; le geste définitif de transition reste à déterminer.

---

# 4. Architecture générale

Le projet est divisé en quatre modules de développement :

1. **Hand Tracking** — Acquisition et interprétation des mouvements.
2. **Audio** — Lecture et transformation musicale.
3. **Environment** — Orchestre, décor et réactions visuelles.
4. **Interaction / Integration** — Gestion des commandes, sélection et communication.

Les modules communiquent à travers des données et des commandes communes.

### Flux général

**Mains du joueur**

↓ Positions, rotations, poses

**Hand Tracking**

↓ Données gestuelles interprétables

**Interaction Manager**

↓ Commandes musicales et événements de sélection

**Système de communication commun**

↙︎ Audio Manager — Visual Manager ↘︎

Les systèmes Audio et Environment reçoivent les commandes pertinentes, puis réagissent indépendamment.

Le module Hand Tracking ne pilote donc pas directement la musique ou l'environnement.

---

# 5. Personne 1 — Hand Tracking

## Responsabilité

Développer le système permettant de suivre les mains du joueur et de reconnaître les gestes nécessaires à l'expérience.

Ce module ne connaît pas le fonctionnement musical de l'orchestre. Son rôle consiste uniquement à fournir des informations gestuelles fiables.

## Fonctionnalités attendues

### Suivi des mains

- Récupérer la position de la main gauche et de la main droite.
- Récupérer l'orientation des poignets.
- Détecter la position des doigts nécessaire aux gestes.
- Identifier si chaque main est correctement suivie.

### Reconnaissance gestuelle

**Main gauche :**
- Reconnaissance de la main ouverte.
- Mesure de sa position verticale.
- Mesure de la rotation du poignet.

**Main droite :**
- Détection de la direction de pointage.
- Reconnaissance du pincement pouce-index.
- Reconnaissance du poing fermé.
- Mesure du déplacement vertical.

### Traitement des mouvements

Les données doivent être suffisamment stables pour permettre une manipulation musicale fluide.

Le module doit notamment éviter les déclenchements répétés d'un même pincement et ne pas envoyer de données obsolètes lorsqu'une main est perdue.

## Entrées du module

| Entrée | Description |
|---|---|
| Position main gauche | Position spatiale fournie par le système XR |
| Orientation main gauche | Rotation du poignet |
| Articulations main gauche | Positions nécessaires à la reconnaissance des poses |
| Position main droite | Position spatiale fournie par le système XR |
| Orientation main droite | Rotation de la main |
| Articulations main droite | Positions nécessaires aux pincements, poings et pointages |
| État de tracking | Disponibilité et fiabilité du suivi de chaque main |
| Référentiel du joueur | Position de référence utilisée pour interpréter les mouvements |

## Sorties du module

| Sortie | Type d'information | Destination |
|---|---|---|
| Hauteur gauche normalisée | Valeur entre 0 et 1 | Interaction |
| Rotation gauche normalisée | Valeur entre 0 et 1 | Interaction |
| État main gauche ouverte | Booléen | Interaction |
| Rayon de pointage droit | Origine et direction spatiales | Interaction |
| Début du pincement droit | Événement ponctuel | Interaction |
| État poing droit fermé | Booléen | Interaction |
| Hauteur droite normalisée | Valeur entre 0 et 1 | Interaction |
| Disponibilité des mains | État de suivi gauche / droite | Interaction |

Les valeurs normalisées doivent rester cohérentes avec le référentiel adopté pour le joueur.

## Livrables

- Système de lecture des mains.
- Système de reconnaissance des gestes.
- Prefab Hand Tracking.
- Scène de test indépendante.
- Données accessibles au module Interaction.

## Validation

Le module est considéré comme fonctionnel lorsque tous les gestes nécessaires au MVP sont détectés de façon suffisamment stable, sans dépendance avec les systèmes audio et environnement.

---

# 6. Personne 2 — Audio

## Responsabilité

Développer le système musical permettant de lire les pistes orchestrales, de contrôler les familles instrumentales et de gérer les transitions vers la techno.

Le module Audio ne reconnaît aucun geste. Il reçoit des commandes musicales et applique les modifications correspondantes.

## Fonctionnalités attendues

### Lecture musicale

- Lire plusieurs pistes orchestrales simultanément.
- Maintenir leur synchronisation.
- Ajouter les pistes électroniques nécessaires.
- Garantir la continuité de la musique lors des modifications.

### Contrôle des familles instrumentales

Pour chaque famille :

- Gérer un volume individuel.
- Activer ou désactiver sa contribution au mixage.
- Conserver sa position dans la composition lors d'un mute.

### Contrôle global

- Modifier le volume de l'ensemble.
- Appliquer un filtre audio global.
- Assurer des variations sonores progressives.

### Gestion des phases

- Lecture orchestrale pour Classical.
- Introduction électronique pour Transition.
- Mixage hybride pour Techno.
- Transitions sonores suffisamment fluides entre les phases.

## Entrées du module

| Entrée | Description |
|---|---|
| Volume global | Valeur normalisée entre 0 et 1 |
| Filtre global | Valeur normalisée entre 0 et 1 |
| Identifiant de famille | Cordes, bois, cuivres ou percussions + Electro|
| Volume individuel | Valeur normalisée entre 0 et 1 |
| État mute | Activé / désactivé |
| Phase musicale | Classical, Transition ou Techno |

Toutes les commandes proviennent du système de communication commun.

## Sorties du module

| Sortie | Description | Destination |
|---|---|---|
| Signal audio final | Musique orchestrale et électronique mixée | Sortie audio XR |
| État des familles | Volumes et états mute réellement appliqués | Systèmes nécessitant un suivi d'état |
| Phase audio active | Phase musicale appliquée | Suivi / débogage |
| État de lecture | Lecture active, arrêtée ou indisponible | Intégration / débogage |

Le système visuel peut réagir aux mêmes commandes que l'audio sans avoir à analyser le signal sonore.

## Ressources nécessaires

- Pistes audio individuelles pour les familles instrumentales.
- Une ou plusieurs pistes électroniques.
- Système de mixage.
- Effet de filtrage global.

Les pistes musicales doivent partager une référence temporelle compatible afin de rester synchronisées.

## Livrables

- Gestionnaire audio principal.
- Gestion des pistes instrumentales.
- Contrôle des volumes et mute.
- Filtre global.
- Gestionnaire des phases musicales.
- Prefab Audio System.
- Scène de test indépendante.

## Validation

Le module est fonctionnel lorsque chaque commande peut être testée indépendamment du hand tracking et produit le comportement sonore attendu.

---

# 7. Personne 3 — Environment / Direction artistique

## Responsabilité

Créer l'environnement visuel du projet et assurer ses réactions aux interactions du joueur et aux changements de phase.

Ce module ne décide pas quelles commandes sont déclenchées. Il reçoit les informations provenant du système Interaction.

## Fonctionnalités attendues

### Salle de concert

- Construire une salle de concert historique simplifiée.
- Mettre en place le podium du chef d'orchestre.
- Installer les éléments architecturaux et décoratifs.
- Prévoir l'éclairage initial.

### Orchestre

- Disposer les quatre familles instrumentales.
- Placer les musiciens et leurs instruments.
- Organiser chaque famille en groupe identifiable.
- Fournir une zone de ciblage pour chaque groupe.

La disposition reprend les principes d'un orchestre classique tout en restant adaptée aux contraintes du projet.

### Feedback visuel

Le joueur doit pouvoir identifier le groupe qu'il cible sans interface flottante.

Un retour visuel doit être prévu pour :
- Le groupe actuellement ciblé.
- Un changement d'état mute.
- Les changements de phase.

### Transformation rave

Mettre en place les éléments visuels de la transition :

- Modification des lumières.
- Apparition des caissons de basses.
- Éclairages colorés.
- Effets visuels électroniques.

L'objectif est de permettre une transformation visible de la salle sans changer de scène.

## Entrées du module

| Entrée | Description |
|---|---|
| Groupe ciblé | Identifiant de la famille actuellement visée |
| Groupe précédemment ciblé | Permet de retirer l'ancien feedback |
| État mute d'une famille | Activé / désactivé |
| Phase active | Classical, Transition ou Techno |
| Intensité globale (optionnelle) | Valeur permettant de moduler l'ambiance visuelle |

Les états audio et visuels doivent rester cohérents.

## Sorties du module

| Sortie | Description | Destination |
|---|---|---|
| Objets de ciblage | Zones de collision associées aux familles | Interaction |
| Identifiants des groupes | Association objet 3D / famille musicale | Interaction |
| Feedback de sélection | Mise en valeur du groupe ciblé | Joueur |
| État visuel des musiciens | Apparence ou animation reflétant leur activité | Joueur |
| Ambiance visuelle | Éclairage et éléments selon la phase | Joueur |

La sortie fonctionnelle principale vers les autres modules est constituée des **objets sélectionnables et de leurs identifiants**.

Les autres sorties sont principalement visuelles.

## Livrables

- Salle de concert.
- Podium du chef.
- Quatre familles instrumentales.
- Zones de sélection.
- Feedback visuel.
- Éléments et éclairages techno.
- Système de transition visuelle.
- Prefab Environment.
- Scène de test indépendante.

## Validation

L'environnement est fonctionnel lorsque les quatre familles peuvent être ciblées et que les changements d'état et de phase produisent une réaction visuelle identifiable.

---

# 8. Personne 4 — Interaction / Integration

## Responsabilité

Assurer la liaison entre les gestes, les instruments, la musique et les effets visuels.

Ce module constitue le centre de décision du projet.

Il est également responsable de l'assemblage de la scène principale Unity.

## Fonctionnalités attendues

### Interprétation des données gestuelles

Recevoir les données du module Hand Tracking et les transformer en commandes musicales.

Les mouvements des mains ne doivent produire une action que lorsque les conditions du geste correspondant sont remplies.

### Sélection des instruments

- Récupérer le rayon de pointage de la main droite.
- Identifier le groupe instrumental visé.
- Transmettre le groupe ciblé au module visuel.
- Conserver la cible pendant une manipulation nécessitant un verrouillage.

### Gestion des commandes

| Donnée gestuelle | Action produite |
|---|---|
| Hauteur main gauche | Changement du volume global |
| Rotation poignet gauche | Changement du filtre global |
| Pointage main droite | Modification de la famille ciblée |
| Début de pincement droit | Inversion de l'état mute du groupe |
| Poing fermé + mouvement vertical droit | Modification du volume individuel |

### Gestion des interactions simultanées

Le joueur peut agir sur plusieurs paramètres.

Le système doit permettre de modifier le volume global avec la main gauche tout en contrôlant une famille avec la main droite.

Il doit également empêcher :
- Les changements involontaires de cible pendant une manipulation.
- Les déclenchements multiples d'un pincement.
- Les commandes lorsque le tracking est perdu.
- Les conflits entre les différents gestes de la main droite.

### Gestion des phases

Centraliser l'activation des phases Classical, Transition et Techno.

Le système transmet les changements de phase aux modules Audio et Environment.

### Intégration générale

- Assembler les prefabs des autres membres.
- Mettre en place la scène principale.
- Connecter les événements.
- Vérifier la cohérence des entrées et sorties.
- Tester l'expérience sur le matériel XR cible.
- Préparer la version de démonstration.

## Entrées du module

| Source | Entrée |
|---|---|
| Hand Tracking | Hauteur gauche |
| Hand Tracking | Rotation gauche |
| Hand Tracking | États des poses de main |
| Hand Tracking | Rayon de pointage droit |
| Hand Tracking | Début de pincement |
| Hand Tracking | Hauteur droite |
| Hand Tracking | Disponibilité du tracking |
| Environment | Colliders et identifiants des familles |
| Contrôle de test | Demande de changement de phase |

## Sorties du module

| Sortie | Destination | Description |
|---|---|---|
| SetGlobalVolume | Audio | Valeur du volume global |
| SetGlobalFilter | Audio | Valeur du filtre global |
| SetGroupVolume | Audio | Volume du groupe sélectionné |
| SetGroupMute | Audio | État mute du groupe |
| SelectionChanged | Environment | Identifiant du groupe ciblé |
| GroupMuteChanged | Environment | État mute d'un groupe |
| ChangePhase | Audio + Environment | Nouvelle phase musicale et visuelle |

Les commandes doivent avoir une signification identique pour tous les modules.

## Livrables

- Gestionnaire principal d'interaction.
- Système de sélection des groupes.
- Communication entre les modules.
- Gestion des priorités gestuelles.
- Gestion centralisée des phases.
- Scène principale intégrée.
- Contrôles de test indépendants du XR.
- Version finale testée.

## Validation

Le module est fonctionnel lorsque les gestes sont correctement traduits en commandes et que les systèmes audio et visuel réagissent de manière cohérente.

---

# 9. Contrat commun entre modules

Cette section définit les informations sur lesquelles tous les membres doivent se mettre d'accord avant de développer leurs systèmes.

## Identifiants communs

**Familles instrumentales :**
- Strings
- Woodwinds
- Brass
- Percussion

**Phases musicales :**
- Classical
- Transition
- Techno

## Convention sur les valeurs

| Paramètre | Plage | Signification |
|---|---|---|
| Volume global | 0 à 1 | 0 = silence, 1 = volume maximal défini |
| Volume individuel | 0 à 1 | 0 = silence, 1 = niveau maximal du groupe |
| Filtre global | 0 à 1 | 0 = filtre fermé, 1 = filtre ouvert |
| Mute | Booléen | True = groupe coupé |
| Groupe ciblé | Identifiant ou aucune cible | Famille actuellement sélectionnée |
| Phase | Classical / Transition / Techno | État de l'expérience |

## Règles de communication

1. Le Hand Tracking transmet des informations gestuelles, jamais des commandes audio.
2. Le module Interaction est responsable de l'interprétation des gestes.
3. Le module Interaction connaît la famille actuellement ciblée.
4. Le module Audio applique les commandes musicales reçues.
5. Le module Environment applique les réactions visuelles.
6. Les changements de phase sont envoyés simultanément aux modules concernés.
7. Les commandes sont indépendantes du mode de saisie : elles doivent pouvoir être déclenchées sans hand tracking pendant les tests.
8. Les modules doivent pouvoir être développés et testés indépendamment.

## Exemple d'interaction complète

**Situation : l'utilisateur souhaite couper les cuivres.**

1. Le joueur pointe les cuivres avec sa main droite.
2. Hand Tracking fournit la direction de pointage.
3. Interaction identifie le groupe Brass.
4. Environment reçoit l'information de sélection et met en évidence les cuivres.
5. Le joueur pince le pouce et l'index.
6. Hand Tracking détecte le début du pincement.
7. Interaction inverse l'état mute du groupe Brass.
8. Audio coupe la contribution sonore des cuivres.
9. Environment actualise leur retour visuel.

Le groupe reste synchronisé avec la composition musicale, même lorsqu'il est inaudible.

---

# 10. Organisation du dépôt Unity

## Structure des dossiers

```text
Assets/
└── _OrchestraRave/
    ├── Scenes/
    │   ├── Main.unity
    │   └── Tests/
    │       ├── Test_Hands.unity
    │       ├── Test_Audio.unity
    │       ├── Test_Environment.unity
    │       └── Test_Interaction.unity
    │
    ├── Scripts/
    │   ├── Core/
    │   ├── HandTracking/
    │   ├── Audio/
    │   ├── Environment/
    │   └── Interaction/
    │
    ├── Prefabs/
    │   ├── Systems/
    │   └── Instruments/
    │
    ├── Audio/
    │   ├── Stems/
    │   └── Mixers/
    │
    ├── Art/
    │   ├── Models/
    │   ├── Materials/
    │   ├── Textures/
    │   ├── Animations/
    │   └── VFX/
    │
    └── Settings/
```

## Répartition des fichiers

| Personne | Modules et dossiers |
|---|---|
| Personne 1 | HandTracking, Test_Hands |
| Personne 2 | Audio, Test_Audio |
| Personne 3 | Environment, Art, Test_Environment |
| Personne 4 | Core, Interaction, Main, Test_Interaction |

Les fichiers du dossier Core sont partagés et doivent être définis collectivement.

## Organisation Git

Le projet utilise un dépôt commun.

Branches de travail :

- `feature/hand-tracking`
- `feature/audio`
- `feature/environment`
- `feature/interaction`

Chaque personne travaille principalement sur ses propres ressources et scènes de test.

La scène `Main.unity` est réservée à l'intégration afin de limiter les conflits.

Les prefabs des modules sont les unités principales de livraison.

---

# 11. Planning de développement

## Jour 1 — Développement des modules

**Début de journée :**
- Initialisation du projet.
- Mise en place du dépôt Git.
- Création des dossiers et scènes de test.
- Définition des entrées/sorties communes.
- Création des premiers prefabs.

**Suite de journée :**
- Développement parallèle des quatre modules.
- Premiers tests indépendants.
- Intégration initiale entre Interaction et Audio.
- Vérification du suivi des mains et des premières commandes.

**Objectif de fin de journée :** pouvoir contrôler au moins un paramètre sonore avec le hand tracking.

## Jour 2 — Intégration et finalisation

**Matin :**
- Finaliser les commandes essentielles.
- Intégrer les quatre familles instrumentales.
- Relier le ciblage aux effets visuels.
- Finaliser les trois phases.

**Après-midi :**
- Intégrer les différents prefabs.
- Tester les contrôles simultanés.
- Vérifier les transitions musicales et visuelles.
- Corriger les interactions instables.
- Préparer la démonstration.

**Objectif final :** une scène unique permettant de manipuler l'orchestre sans interface, puis d'observer sa transformation en rave.

---

# 12. Critères de réussite du MVP

Le projet est considéré comme terminé lorsque :

- [ ] Les mains du joueur sont détectées.
- [ ] Le volume global est contrôlable avec la main gauche.
- [ ] Le filtre global est contrôlable avec le poignet gauche.
- [ ] La main droite permet de cibler les familles instrumentales.
- [ ] Un pincement permet de couper ou réactiver le groupe ciblé.
- [ ] Le volume d'un groupe peut être modifié indépendamment.
- [ ] Les pistes audio restent synchronisées.
- [ ] Les quatre familles sont représentées dans la scène.
- [ ] Le groupe ciblé est identifiable visuellement.
- [ ] Les trois phases musicales fonctionnent.
- [ ] Les trois phases visuelles fonctionnent.
- [ ] Le changement de phase conserve la continuité musicale.
- [ ] L'expérience est utilisable sans menu.
- [ ] Le prototype fonctionne sur le matériel XR cible.

---

# 13. Fonctionnalités envisagées après le MVP

Les fonctionnalités suivantes ne sont pas prioritaires pour les deux jours de développement :

- Baguette de chef d'orchestre saisissable et relâchable.
- Gestes spécifiques avec la baguette.
- Contrôles combinant les deux mains.
- Gestion de la distance entre les mains.
- Effets de delay, réverbération, distorsion et pitch.
- Boucles et remix en temps réel.
- Animations avancées des musiciens.
- Réactions visuelles synchronisées précisément avec les battements musicaux.
- Transitions plus complexes entre compositions.
- Évolution dynamique de l'architecture de la salle.

Ces fonctionnalités pourront être ajoutées sans remettre en cause le découpage principal des modules.

---

**Orchestra Rave — Un orchestre baroque qui dérape en rave party.**
