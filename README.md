# CatLock 🐱🔒

Petit utilitaire Windows qui empêche le chat de faire n'importe quoi en marchant sur le clavier ou la souris.

- **Alt+Shift+W** (par défaut, modifiable) : verrouille le clavier **et** la souris (mouvements, clics, molette).
- Le même raccourci à nouveau : tout est réactivé. Le raccourci reste écouté même quand tout est bloqué.
- Vit dans les icônes cachées de la barre des tâches (tête de chat orange = libre, rouge avec cadenas = verrouillé).
- Un petit bandeau en bas à droite indique quand c'est verrouillé.
- Se lance tout seul au démarrage de Windows (désactivable via clic droit sur l'icône).

## Utilisation

Lancez `bin\CatLock.exe` une fois : il s'inscrit au démarrage de Windows et reste dans la zone de notification.
Clic droit sur l'icône :
- *Verrouiller clavier et souris*
- *Changer le raccourci...*
- *Lancer au démarrage de Windows* (coché par défaut)
- *Quitter CatLock*

### Changer le raccourci

*Changer le raccourci...* ouvre une petite fenêtre : appuyez sur la nouvelle combinaison, puis **Enregistrer**.

- Il faut au moins une touche **Ctrl, Alt, Shift ou Win**, plus une autre touche (une touche seule serait trop facile à déclencher pour le chat).
- Ctrl+Alt+Suppr et Win+L sont réservés à Windows et refusés.
- **Par défaut** remet Alt+Shift+W.
- Le raccourci est enregistré dans le registre (`HKCU\Software\CatLock`) et conservé au redémarrage.

Le démarrage automatique passe par une **tâche planifiée** « CatLock » (déclenchée à l'ouverture de session, sans droits admin, visible dans le Planificateur de tâches). Elle pointe vers l'emplacement actuel de l'exe : si vous le déplacez, relancez-le une fois depuis son nouvel emplacement et le chemin est mis à jour automatiquement.

## Compiler

Aucun outil à installer : le compilateur C# du .NET Framework 4 est fourni avec Windows.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Le script arrête CatLock s'il tourne, puis produit `bin\CatLock.exe`.

## Bon à savoir

- Tant que CatLock tourne, **le raccourci n'est plus transmis aux autres applications**.
- Les modificateurs doivent correspondre exactement : avec Alt+Shift+W, les combinaisons Ctrl+Alt+Shift+W, AltGr+Shift+W (caractères spéciaux) ou Win+Alt+Shift+W ne déclenchent rien.
- Relâcher les modificateurs du raccourci ne déclenche rien d'autre : ni barre de menus (Alt), ni menu Démarrer (Win), ni changement de langue du clavier (Alt+Shift).
- **Ctrl+Alt+Suppr** n'est jamais bloqué (Windows l'interdit), c'est la sortie de secours.
- Un écran tactile peut ne pas être bloqué : seuls clavier, souris et pavé tactile le sont.

## Fonctionnement

Hooks Windows bas niveau (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`) : en mode verrouillé, chaque événement est avalé avant d'atteindre Windows, sauf le raccourci. Les hooks sont réinstallés toutes les 30 s (Windows peut les retirer sans prévenir), et l'état des touches est remis à zéro après un verrouillage de session ou une sortie de veille. La fenêtre de changement de raccourci capture la combinaison via ce même hook, ce qui permet aussi les combinaisons avec la touche Win.
