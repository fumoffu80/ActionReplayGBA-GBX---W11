# ActionReplayGBX — Parité fonctionnelle v1.2.16 → port C# v1.2.31.2

Source de référence : `ActionReplayGBX_W11_Source_v1.2.16.zip`.
Objectif : conserver le moteur/protocole C# validé sur matériel, tout en rétablissant l'ergonomie et les fonctions visibles de la v1.2.16.

> État mis à jour d'après le source v1.2.31.2 et le CI. Une case cochée signifie que la fonction est présente dans le port ou couverte par un test automatisé. Les validations qui nécessitent un PC/port USB neuf ou une écriture firmware réelle restent volontairement non cochées.

## Interface / disposition
- [x] Fenêtre unique classique, pas d'onglets.
- [x] Taille minimale ~900×700, redimensionnement DPI dynamique.
- [x] En-tête `Action Replay GBX v...`.
- [x] Bouton FR/EN visible.
- [x] Bloc `JEU CONNECTÉ` à gauche.
- [x] Jaquette GBA 92×120 lorsque disponible.
- [x] Nom jeu, Game ID, version USB, espace libre AR.
- [x] Bloc `SAUVEGARDE DU JEU CONNECTÉ` à droite.
- [x] Ligne rouge de recommandation complète lorsqu'AR ne répond pas.
- [x] Barre d'actions principale : Lire/actualiser AR, Écrire AR, Importer XPC, Exporter XPC, Choix bibliothèque, Pilote, Sauvegarde Firmware, Mise à jour Firmware, Dossier.
- [x] Annuler/Rétablir visibles.
- [x] Bouton supplémentaire `Journal / outils` conservé dans le port C#.
- [x] Quatre listes visibles simultanément : Bibliothèque PC jeux/codes, Action Replay jeux/codes.
- [x] Rail central PC→AR / AR→PC.
- [x] Éditeur de code en bas.
- [x] Barre de progression transfert et barre de mémoire AR toujours visibles.
- [x] Ligne d'état basse toujours visible.

## Langue
- [x] Choix Français / English au premier lancement.
- [x] Persistance dans le profil Windows, commune installé/portable.
- [x] Bouton FR/EN pour changer ensuite.

## Bibliothèques PC
- [x] Datel 3.3 officielle (173 jeux / 1886 codes), compte vérifié par test CI.
- [x] Europe MAX v7 (227 jeux / 2605 codes), compte vérifié par test CI et avertissement compatibilité.
- [x] Bibliothèques personnalisées nommées, créées vides.
- [x] Liste des bibliothèques personnalisées dans `Choix bibliothèque`.
- [x] Réinitialisation de la bibliothèque active à son état d'origine.
- [x] Persistance de la bibliothèque choisie.
- [x] Noms longs conservés côté PC.
- [x] Conversion déterministe AR-safe ≤20 octets uniquement au transfert/écriture.

## Listes, cases et sélection
- [x] Cases à cocher côté PC pour transfert/fusion.
- [x] Cases à cocher côté AR pour fusion.
- [x] Cocher un jeu fait apparaître tous ses codes comme cochés.
- [x] Cocher des codes individuels conserve le master code automatiquement lors du transfert.
- [x] Jeu/code déjà présent dans l'AR affiché comme déjà sélectionné/présent côté PC.
- [x] Touche Suppr sur les quatre listes.
- [x] Clic droit sélectionne d'abord la ligne visée.
- [x] Tri alphabétique permanent, insensible à la casse/accents, couvert par test CI.

## Menus clic droit — jeux
- [x] Cocher/décocher.
- [x] PC : envoyer ce jeu vers l'AR.
- [x] AR : copier ce jeu vers bibliothèque PC.
- [x] Modifier dans l'éditeur.
- [x] Exporter ce jeu en XPC.
- [x] Supprimer ce jeu.
- [x] Nouveau jeu.
- [x] Nouveau code dans ce jeu.
- [x] Fusionner les jeux cochés.
- [x] Fusionner jeux identiques (nom/master).
- [x] Fusionner par master strict identique avec aperçu.

## Menus clic droit — codes
- [x] Cocher/décocher code.
- [x] PC : envoyer ce code vers AR avec ajout automatique du master si nécessaire.
- [x] AR : copier ce code vers PC.
- [x] Modifier dans l'éditeur.
- [x] Exporter ce code en XPC.
- [x] Supprimer ce code.
- [x] Nouveau jeu / nouveau code.
- [x] Fonctions de fusion accessibles.

## Éditeur
- [x] Nouveau jeu / nouveau code.
- [x] Modification nom jeu / nom code / données.
- [x] Case Code maître : force `(M)` et verrouille le nom.
- [x] Bouton Enregistrer les modifications.
- [x] Bouton Annuler visible si création ou modification non enregistrée.
- [x] Confirmation enregistrer / abandonner / revenir si changement de sélection avec édition sale.
- [x] Normalisation automatique du texte en `XXXXXXXX YYYYYYYY`, une paire par ligne, à la perte de focus.
- [x] Défilement horizontal, pas de wrap.
- [x] Undo/redo jusqu'à 50 actions de données.

## Import / export XPC
- [x] Import XPC avec choix destination PC ou AR.
- [x] Glisser-déposer `.xpc`.
- [x] Glisser-déposer `.bin` vers la vue AR hors ligne.
- [x] Export bibliothèque/base AR entière.
- [x] Export jeu sélectionné.
- [x] Export code sélectionné.
- [x] Détection des noms >20 octets / non Latin-1.
- [x] Correction manuelle ou automatique avec aperçu avant import.
- [x] Fusion/dédoublonnage conforme au modèle porté, avec tests round-trip/master/dédoublonnage.

## Jaquette en ligne
- [x] Identification par Game ID GBA.
- [x] GameDB-GBA `release_name.txt` puis `title.txt`.
- [x] Fallback sur nom renvoyé par l'AR.
- [x] Named_Boxarts Libretro.
- [x] Fallback `thumbnails.libretro.com` puis GitHub libretro-thumbnails.
- [x] Cache `%LOCALAPPDATA%\ActionReplayGBX\Cache\BoxArt`.
- [x] Échec silencieux si aucune image.

## USB / connexion
- [x] WMI détecte `VID_05FD&PID_DAAE`.
- [x] Moteur/protocole C# validé matériel : info, codes, SAVE, dump Flash.
- [x] Après installation pilote : attendre réénumération Windows puis revérifier automatiquement.
- [x] Vérifier explicitement service WinUSB + DeviceInterfaceGUID(s) + `engine info` dans la GUI après réparation via `V12312ParityBridge`.
- [x] Réessai automatique normal.
- [x] Après vrai timeout : récupération des pipes via `engine info --recover`.
- [x] Si insuffisant : temporisation de 6 s puis redémarrage PnP Windows automatique via le helper `--restart-only`, sans réinstallation du pilote ; une élévation UAC peut être demandée et n'est pas répétée en boucle si elle échoue/est annulée.
- [x] Après récupération des pipes réussie, l'état `busy` de la GUI est explicitement libéré avant la reconnexion normale.
- [x] Surveillance périodique WMI sans trafic protocolaire destructif.
- [x] Affichage distinct USB présent / protocole répond.
- [x] Ligne d'état de connexion dédiée.

## Progression / mémoire
- [x] Progression dynamique lecture codes quand le moteur émet fraction/pourcentage.
- [x] Progression dynamique écriture codes quand le moteur émet fraction/pourcentage.
- [x] Progression firmware quand le moteur émet fraction/pourcentage.
- [x] Progression SAVE réelle : le moteur émet `octets transférés / total` tous les 1 Kio en lecture et écriture, sans modifier le protocole USB.
- [x] Barre mémoire AR basée sur `blob utilisé + remaining storage`.
- [x] Texte occupation `N / capacité, %`.
- [x] Activité détaillée dans la ligne d'état.

## SAVE
- [x] Dump 64 Kio moteur validé.
- [x] Écriture SAVE moteur validée matériel avec redump identique.
- [x] Export/restauration accessibles depuis le bloc du jeu connecté.
- [x] Validation taille 65536 octets avant restauration.
- [ ] Fidélité exacte des libellés/messages v1.2.16 à comparer visuellement.

## Firmware
- [x] Dump Flash 256 Kio moteur validé matériel.
- [x] Validation GSAU/TEA/CRC32/firmware exécutable.
- [x] Préflight lecture seule validé contre v4.0 EU/FRA.
- [x] UI sauvegarde / validation / mise à jour présente.
- [x] Backup complet annoncé comme obligatoire avant toute écriture par le flux moteur.
- [x] Avertissements et double confirmation explicites.
- [x] `.gsu` Datel 0x20008, BIN 128 Kio, BIN 256 Kio acceptés ; BIN 512 Kio explicitement refusé — matrice CI hors ligne validée.
- [x] Ne pas tester l'écriture firmware tant que le reste n'est pas stabilisé.

## Pilote
- [x] Bootstrap libwdi local, hash vérifié, aucun téléchargement runtime.
- [ ] Installation initiale sur PC neuf : code présent, validation réelle à faire.
- [ ] Réparation nouveau port USB : code présent, validation réelle à faire.
- [x] Réécriture GUID après réénumération si Windows le perd, implémentée dans le helper.
- [x] Boucle attente/retry avant de conclure à l'échec dans la GUI.
- [x] Annulation/échec du helper Pilote : arrêt immédiat du flux GUI, sans faux message « pilote appliqué » ni 8 réessais inutiles.
- [x] Test final par `engine info` dans la GUI.
- [x] Test final combiné `WinUSB + GUID + engine info` dans la GUI via le pont v1.2.31.2.

## Journal / outils
- [x] Conservé dans le port C#.
- [x] Afficher log d'opérations.
- [x] Chemin du log.
- [x] Statut WMI/WinUSB de base.
- [x] Statut GUID explicite via le bouton `WinUSB / GUID` injecté dans Journal / outils.
- [x] Boutons diagnostics lecture seule (`engine info`, `WinUSB / GUID`, actualisation, copie du log).
- [x] Ouvrir dossier données/logs/backups/cache.
- [x] Détails techniques des erreurs conservés avec chemin du journal.

## Packaging / AV
- [x] Aucun ancien EXE/DLL versionné dans l'archive v1.2.31.1.
- [x] Même règle pour v1.2.31.2 : artefact CI direct contrôlé, uniquement GUI/driver/model/engine v1.2.31.2.
- [x] `wdi-simple.exe` est embarqué et hash-vérifié ; aucun téléchargement pilote à l'exécution.
- [x] Pas d'obfuscation/packing applicatif dans le build C#.
- [x] Inno Setup sans compression pendant stabilisation.
- [ ] Signer et soumettre les faux positifs une fois l'architecture figée.

## Validation CI v1.2.31.2
- [x] Compilation GUI v1.2.31.2.
- [x] Compilation Model DLL v1.2.31.2.
- [x] Compilation Driver helper v1.2.31.2.
- [x] Compilation Engine v1.2.31.2.
- [x] 10/10 tests modèle XPC réussis.
- [x] Bases Datel/MAX v7 vérifiées par nombre de jeux/codes et round-trip sémantique.
- [x] Matrice firmware hors ligne : GSU + 128 Kio + 256 Kio acceptés, 512 Kio / taille non supportée / payload vide refusés.
- [x] Build installateur Inno réussi.
- [x] Build portable réussi.
- [x] Manifeste SHA-256 cohérent sur les fichiers de l'artefact direct.
- [ ] Validation matérielle utilisateur de cette build v1.2.31.2 avant toute fusion.
