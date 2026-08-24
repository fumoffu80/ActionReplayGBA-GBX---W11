# ActionReplayGBX v1.2.31.4

Correctif ciblé : restauration réelle de l'affichage des jaquettes de jeux.

Validation obligatoire avant livraison :
- TLS 1.2 explicite pour les téléchargements HTTPS de jaquettes ;
- téléchargement réseau réel d'une jaquette connue (BPRE / Pokémon FireRed) dans le CI Windows ;
- fichier image valide non vide ;
- affectation d'une Image non nulle au PictureBox ;
- visibilité du PictureBox quand un jeu connecté possède un ID valide ;
- journalisation des erreurs de téléchargement au lieu de les ignorer silencieusement ;
- conservation de la parité visuelle v1.2.16, de Journal / Outils et d'Effacer log ;
- 4 livrables séparés : Setup, Portable, Source, SHA256.

Ne pas fusionner vers main avant validation utilisateur.
