# Direction artistique — DJ-Maestro

Responsable : Vitaly. Livraison : vendredi fin d'après-midi.
Principe : un orchestre en tenue médiévale, éclairé à la torche, qui bascule en rave techno quand les caissons tombent du ciel.
Le contraste est le gag ; pas besoin d'un univers cohérent, besoin de deux états visuels nets.

## Les deux états

| | Médiéval (par défaut) | Rave (après le drop) |
|---|---|---|
| Lumière | Chaude, basse, torches (orange / ambre), ombres douces | Froide et saturée : magenta, cyan, UV. Spots colorés qui balayent, brume |
| Matières | Bois, pierre, tissu, cuir, laiton | Métal noir, néons émissifs, chrome |
| Sol / fond | Dalles de pierre, tentures, bannières | Idem mais noyé dans la brume et les spots |
| Musiciens | Tenues médiévales, instruments d'époque, animation « joue » calme | Mêmes modèles, animation « headbang », lunettes de soleil si le temps le permet |
| Nouveaux objets | — | Caissons (subwoofers) qui s'écrasent sur scène, stroboscope lent |

La baguette est l'exception voulue : futuriste (pointe émissive, matière chrome) posée sur un pupitre en bois.

## Disposition de la scène (unités Unity = mètres)

- Joueur debout à l'origine, regard vers +Z. Podium rond ou carré sous ses pieds (~1 m).
- Pupitre à z = 1 m, plateau à y = 1,1 m, incliné ~30° vers le joueur. Socle de la baguette sur son coin droit.
- Musiciens sur **trois plateformes étagées** en arc, à 5–8 m : cordes en bas (y ≈ 0), vents au milieu (y ≈ 1), percussions en haut (y ≈ 2).
  L'étagement sert au rayon de la baguette (cibles séparées verticalement), garder au moins 1,5 m entre deux pupitres.
- Caissons : point de chute visible devant les musiciens, entre eux et le pupitre (z ≈ 3–4 m), hors de la trajectoire du rayon au repos.
- Fond : mur de pierre avec tentures à 10–12 m, ou nuit étoilée. Rien d'utile derrière le joueur.

## Liste d'assets par priorité

P0 — sans ça, pas de démo vendredi :
1. Baguette (~25 cm, futuriste, pointe émissive) + socle. Pivot à la poignée, +Z vers la pointe.
2. Pupitre en bois avec plateau plat où le canvas des devs se pose (zone ~50 × 35 cm).
3. Sol de scène (pierre) + podium + 3 plateformes étagées.
4. 5 à 6 musiciens low-poly en tenue médiévale, chacun avec un instrument, animation « joue » en boucle (idle musical).
   Au minimum : 2 cordes (luth / vièle), 2 vents (flûte / cornemuse), 1 percussion (tambour), 1 au choix.
5. 2 à 4 caissons noirs avec animation de chute + rebond, et un cône de lumière magenta/cyan qui s'allume après la chute.
6. Éclairage : 3–4 torches (point lights ambrées, pas de shadow) pour le médiéval ; 3–4 spots colorés rotatifs pour la rave.

P1 — si P0 est fini jeudi :
7. Animation « headbang » sur les musiciens (déclenchée au drop).
8. Brume (particules peu nombreuses, grosses, lentes) et bannières / tentures sur le mur.
9. Traînée lumineuse derrière la pointe de la baguette (Trail Renderer, matière émissive).
10. Highlight du pupitre visé : variante de matériau avec émission, ou outline.

P2 — après la deadline (idées validées, ne pas commencer avant vendredi soir) :
11. Lutin qui danse, faille spatio-temporelle, stroboscope.

## Contrat art ↔ code

Les devs instancient ces prefabs par leur nom exact ; ne pas les renommer sans prévenir.

| Prefab (`Assets/_OrchestraRave/Prefabs/`) | Contenu | Ce que le code attend |
|---|---|---|
| `Environment` | Toute la scène décor : sol, podium, plateformes, mur, torches, spots (éteints), caissons (cachés en hauteur) | Un seul objet racine à poser dans la scène principale |
| `Baguette` | Modèle + collider capsule + Rigidbody | Un enfant nommé `Tip` à la pointe (origine du rayon) |
| `SocleBaguette` | Support | Un enfant nommé `RestPoint` (position / rotation de repos) |
| `Pupitre` | Meuble | Un enfant nommé `PanelAnchor` sur le plateau (le canvas des devs s'y attache) |
| `Musicien_Cordes`, `Musicien_Vents`, `Musicien_Percu` (…) | Personnage + instrument + Animator | Triggers Animator : `Play`, `Stop`, `Headbang`. Un `Renderer` racine pour le highlight |
| `Caissons` | Groupe de caissons + leurs spots | Trigger Animator `Drop` (chute + allumage des spots) |

Chaque musicien est un prefab séparé pour que le code puisse les activer / couper un par un.

## Contraintes techniques (Quest 3, URP Mobile)

- Budget scène complète : ~300 000 triangles max, ~100 draw calls, 60 fps en RenderScale 0,8 (réglage actuel de `Mobile_RPAsset`).
- Un musicien ≤ 10 000 triangles, un prop ≤ 2 000. Low-poly assumé, le style le permet.
- Textures : 1024² max par objet, 2048² pour un atlas de décor. Compression ASTC à l'import (réglage Android).
- Shaders : `URP/Lit` ou `URP/Simple Lit` uniquement. Pas de shader custom sauf accord.
- Lumières temps réel : une directionnelle avec ombres au maximum, le reste sans ombres. Préférer l'émissif aux lumières.
- Lumière médiévale : baker les lightmaps du décor statique (`Environment` en Static), les musiciens en Light Probes.
- Transparence : la brume et les cônes de spot sont les seuls objets transparents. Pas de verre, pas de feuilles alpha en masse.
- Particules : < 200 particules vivantes au total.
- Animations : Animator avec 3 états maximum par personnage. Les clips Mixamo s'importent en Humanoid.

## Sources d'assets gratuits (vérifier la licence à chaque fois, la noter dans `Docs/CREDITS.md`)

- Personnages et props low-poly : Quaternius (CC0), Kenney (CC0), poly.pizza (agrégateur CC0), Sketchfab avec filtre « downloadable » + licence CC0 / CC-BY.
- Textures PBR : ambientCG (CC0), Poly Haven (CC0).
- Animations : Mixamo (gratuit avec un compte Adobe) pour « playing instrument » (chercher guitar / drums / flute), « headbang », idles.
- Instruments médiévaux : Sketchfab (luth, vièle, cornemuse, tambour) ou modélisation rapide dans Blender à partir de primitives.
- Fonts médiévales pour le panneau de commandes : Google Fonts (Uncial Antiqua, MedievalSharp, Almendra).

## Workflow git

1. `git lfs install` une fois sur la machine, puis `git checkout -b da/vitaly` depuis `main` à jour.
2. Travailler dans `Assets/_OrchestraRave/Art/`, `Assets/_OrchestraRave/Prefabs/` et `Assets/_OrchestraRave/Scenes/Tests/Art_Sandbox.unity` uniquement. Ne jamais ouvrir-modifier `Scenes/MainScene.unity`.
3. Commits petits et fréquents (`art: baguette v1`, `art: musiciens cordes + anim Play`), push en fin de demi-journée.
4. Quand un prefab est prêt : PR vers `main` avec une capture d'écran. Les devs le posent dans la scène de leur côté.
5. Avant chaque PR : `git pull --rebase origin main` sur la branche, et vérifier que le projet ouvre sans erreur Console.
