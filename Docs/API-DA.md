# API de la DA — `MaestroOrchestraApi`

Un seul point d'entrée pour déclencher tout ce que la DA expose : la séquence rave, les lumières, les musiciens et les caissons.
Classe statique, namespace `Maestro.Environment`. Rien à poser dans la scène : il suffit d'appeler les méthodes.

```csharp
using Maestro.Environment;
MaestroOrchestraApi.StartRaveSequence();
```

Exemple complet à recopier : `Assets/_OrchestraRave/Scripts/Environment/Examples/RaveGestureBridge.cs`.

## 1. Poser les prefabs dans `MainScene`

L'API cherche ses objets **par nom de GameObject racine** dans la scène active. Chaque prefab se pose donc à la racine de la scène, une seule fois, sous son nom exact : pas de parent, pas de renommage, pas de suffixe « (1) ».

| Prefab | Dossier (`Assets/_OrchestraRave/Prefabs/`) | Position | Rotation Y |
|---|---|---|---|
| `Environment` | racine | (0, 0, 0) | 0° |
| `Strings` | `Instruments/` | ancre `Anchor_Strings` : (0, −0.5, 9) | 180° |
| `Vent` | `Instruments/` | ancre `Anchor_Vent` : (−5, 1.5, 11) | 180° |
| `Brass` | `Instruments/` | ancre `Anchor_Brass` : (5, 1.5, 11) | 180° |
| `Percution` | `Instruments/` | ancre `Anchor_Percution` : (0, 3.5, 14.5) | 180° |
| `Speakers` | racine | ancre `Anchor_Speakers` : (0, −1.5, 3) | 0° |
| `StageLights`, `Lasers`, `VFX` | racine | (0, 0, 0) | 0° |
| `DebugPanel` (outil de test, facultatif) | `Systems/` | (0, 0, 0) | 0° |

Les ancres sont des objets vides sous `Environment` : copier leur position et leur rotation monde sur l'instance correspondante. Le joueur se tient à l'origine et regarde vers +Z. `VFX` porte le compte à rebours et le flash : sans lui, la séquence tourne quand même, sans compte à rebours visible. Aucune référence à câbler : la séquence retrouve elle aussi les autres prefabs par leur nom.

Contrôle : `Debug.Log(MaestroOrchestraApi.Diagnose());` en Play Mode doit afficher toutes les lignes en `OK`.

## 2. Méthodes

Toutes les méthodes sont statiques, sans effet si on les rappelle, et sûres à tout moment. Si une cible manque, un warning la nomme et l'appel ne fait rien. `section` vaut `"Strings"`, `"Vent"`, `"Brass"` ou `"Percution"`, sans tenir compte de la casse. `null` désigne toutes les sections.

| Membre | Effet | Exemple |
|---|---|---|
| `IsReady` | Vrai si toutes les cibles sont trouvées et actives. | `if (MaestroOrchestraApi.IsReady) …` |
| `Diagnose()` | Texte listant les cibles trouvées et manquantes. | `Debug.Log(MaestroOrchestraApi.Diagnose());` |
| `StartRaveSequence()` | Lance la séquence complète. Ignoré si elle tourne déjà. | `MaestroOrchestraApi.StartRaveSequence();` |
| `AbortRaveSequence()` | Arrête tout et revient à l'opéra : lumières de fête éteintes, musiciens au repos, caissons remontés. | `MaestroOrchestraApi.AbortRaveSequence();` |
| `IsRaveSequenceRunning` | Vrai du lancement jusqu'à l'arrêt, Party comprise. | `if (!MaestroOrchestraApi.IsRaveSequenceRunning) …` |
| `RavePhase` | `"Idle"`, `"Countdown"`, `"Drop"`, `"Transform"` ou `"Party"`. | `string p = MaestroOrchestraApi.RavePhase;` |
| `OnRavePhaseChanged` | Event appelé à chaque changement de phase. | `MaestroOrchestraApi.OnRavePhaseChanged += p => Debug.Log(p);` |
| `DropSpeakers()` | Chute des caissons seule. Ignoré pendant la séquence ou si les caissons sont déjà tombés. | `MaestroOrchestraApi.DropSpeakers();` |
| `TransformToRave()` | Théâtre vers château de nuit, en 6 s. Ignoré pendant la séquence. | `MaestroOrchestraApi.TransformToRave();` |
| `ResetToOpera()` | Retour au théâtre, en 6 s. Pendant la séquence, équivaut à `AbortRaveSequence()`. | `MaestroOrchestraApi.ResetToOpera();` |
| `SetParty(bool)` | Tout le bloc fête : néons, lasers, spots, pulsation, notes, headbang. Les néons ne s'allument qu'en rave. | `MaestroOrchestraApi.SetParty(true);` |
| `SetLasers(bool)` | Faisceaux laser. | `MaestroOrchestraApi.SetLasers(false);` |
| `SetStageLights(bool)` | Spots de scène avec leur balayage magenta et cyan. | `MaestroOrchestraApi.SetStageLights(true);` |
| `SetPoursuites(bool)` | Poursuites d'opéra. Effet visible à l'opéra seulement. | `MaestroOrchestraApi.SetPoursuites(false);` |
| `MusiciansPlay(section)` | Les musiciens jouent. | `MaestroOrchestraApi.MusiciansPlay("Strings");` |
| `MusiciansStop(section)` | Retour à la pose de repos. | `MaestroOrchestraApi.MusiciansStop();` |
| `MusiciansHeadbang(section)` | Headbang. | `MaestroOrchestraApi.MusiciansHeadbang("brass");` |
| `HighlightSection(section, bool)` | Surbrillance douce d'une section. | `MaestroOrchestraApi.HighlightSection("Vent", true);` |
| `ClearHighlights()` | Éteint toutes les surbrillances. | `MaestroOrchestraApi.ClearHighlights();` |
| `Sections` | Les noms de sections valides. | `foreach (var s in MaestroOrchestraApi.Sections) …` |
| `Beat()` | Un temps de l'horloge audio, voir la partie 5. | `MaestroOrchestraApi.Beat();` |

Pendant la séquence, les lumières, les musiciens et les surbrillances s'appliquent tout de suite et la séquence continue. Elle rallume le bloc fête en entrant en Party et éteint tout à l'arrêt. Un nom de section inconnu produit un warning qui liste les sections valides.

## 3. La séquence rave, phase par phase

| Phase | Durée | Ce qui se passe |
|---|---|---|
| `Countdown` | 3,4 s | « 3 », « 2 », « 1 » à 2 m devant le joueur, une seconde chacun, avec un flash blanc. Les lumières d'opéra baissent d'un quart à chaque chiffre. Puis « DROP » pendant 0,4 s. |
| `Drop` | 2,5 s | Les quatre caissons tombent sur les flancs de la scène. |
| `Transform` | 1 s + 6 s | Une seconde après l'impact, le théâtre se transforme en château de nuit. |
| `Party` | jusqu'à l'arrêt | Lasers, spots en balayage, ambiance néon, néons en cascade, caissons qui pulsent, notes, headbang général. |
| `Idle` | — | Après `AbortRaveSequence()` ou `ResetToOpera()` : tout s'éteint et le décor revient à l'opéra en 6 s. |

Party arrive environ 13 s après `StartRaveSequence()`. `OnRavePhaseChanged` reçoit `Countdown`, `Drop`, `Transform`, `Party`, puis `Idle` à l'arrêt.

## 4. Triggers Animator bas niveau

Pour ceux qui préfèrent piloter les Animators directement, sans passer par l'API :

| Objet | Triggers |
|---|---|
| `Environment` | `Rave` (vers le château), `Reset` (vers l'opéra) |
| `Speakers` | `Drop` (chute des caissons) |
| Chaque `Musician_N` des sections | `Play`, `Stop`, `Headbang` |

Un trigger envoyé alors que l'Animator est déjà dans l'état visé reste en attente et part plus tard. L'API évite ce piège. En direct, faites un `ResetTrigger` des autres triggers avant chaque `SetTrigger`.

## 5. `Beat()` et l'horloge audio

Appelez `MaestroOrchestraApi.Beat()` à chaque temps de la musique. Dès le premier appel, l'horloge interne des caissons, fixée à 128 BPM, se coupe et les caissons suivent vos temps. Ils ne pulsent qu'en Party : hors Party, l'appel ne fait rien de visible, mais il reste sûr.

```csharp
// Dans Update, avec l'horloge DSP :
if (AudioSettings.dspTime >= nextBeat) { MaestroOrchestraApi.Beat(); nextBeat += 60.0 / bpm; }
```

## 6. `Diagnose()` et panneau de debug

`MaestroOrchestraApi.Diagnose()` renvoie une ligne par cible avec `OK` ou `MANQUANT` et la raison : racine introuvable, désactivée ou composant absent. `VFX` y apparaît comme facultatif. Le panneau de debug logge ce diagnostic au démarrage.

Avec le prefab `DebugPanel`, dans l'éditeur, `F1` affiche ou cache le panneau et `F2` lance la séquence rave. En VR, joindre les mains 5 s affiche le panneau, et faire les cornes des deux mains 1,5 s lance la séquence. Les boutons « Séquence rave » et « Abort » appellent `MaestroOrchestraApi.StartRaveSequence()` et `MaestroOrchestraApi.AbortRaveSequence()`, comme votre code. Les autres boutons : Rave, Reset, Drop caissons, Musiciens Play, Stop et Headbang, Spots, Lasers et Poursuites on/off, Highlight test, Fermer.
