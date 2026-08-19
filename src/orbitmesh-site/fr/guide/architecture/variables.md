# Variables

Des valeurs nommées (Console → Variables) que les settings de n'importe quel package peuvent référencer avec un token `{Nom}`, n'importe où dans une valeur - y compris à l'intérieur d'un setting JSON. Marche que le package expose un setting plat `Latitude` ou l'enfouisse dans un bloc de config plus large.

Changez une Variable une fois. Chaque package qui la référence récupère le changement - aucune édition par package.

## Secrets

Une Variable peut être marquée secrète. Sa valeur est chiffrée au repos avec le même chiffrement que les identifiants Machine. La Console n'affiche jamais la valeur d'un secret dans la liste - seulement derrière un "Reveal" explicite.

## Quand la substitution a lieu

Uniquement à la livraison, quand les settings atteignent un package connecté. Jamais quand la Console les lit ou les écrit. Un token `{Nom}` reste du texte littéral en stockage. Éditer les settings d'un package ne peut pas accidentellement le détacher de la Variable qu'il suit.

Voir [Settings](/fr/guide/sdk/settings) pour le point de vue côté package.
