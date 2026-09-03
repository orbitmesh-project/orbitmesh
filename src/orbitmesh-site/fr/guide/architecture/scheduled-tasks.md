# Tâches planifiées

![Page Scheduled Tasks - formulaire d'ajout et tableau des tâches configurées avec cron, cible et dernière exécution](/screenshots/scheduled-tasks.jpg)

Envoie un message selon un horaire cron (Console → Administration → Scheduled Tasks) - l'équivalent automatisé d'un humain qui ouvre la page Messages et clique sur Invoke.

## Comment ça s'exécute

Chaque tâche s'exécute au nom d'un identifiant choisi, en passant par les mêmes vérifications que n'importe quel autre expéditeur : `Authorizations` (Allow/Deny par cible) et le scope `messages:execute`. Une tâche planifiée est un appelant de plus du modèle de permissions, pas un moyen de le contourner - si l'identifiant ne peut pas envoyer le message à la main, la tâche planifiée ne le peut pas non plus.

## Choisir la cible

Edge, Package et le handler de message sont des menus déroulants alimentés par les packages réellement connectés et leurs `[MessageHandler]` effectivement déclarés - les mêmes données que montre la page Messages - pas des noms tapés librement. Les paramètres sont générés à partir de la signature réelle du handler, pour éliminer une clé ou un paramètre mal orthographié qui passerait inaperçu dans un envoi déclenché par une minuterie, sans supervision.

## Exécutions manquées

Une occurrence manquée pendant que le Server était éteint (mise à jour, crash, réseau...) est ignorée par défaut - la prochaine occurrence normale s'exécute quand même comme prévu. Active "Catch up if missed" pour déclencher un seul envoi de rattrapage à la place, qui remet l'état en cohérence - jamais un envoi par occurrence manquée.

## Syntaxe cron

Cron standard à 5 champs (minute heure jour mois jour-de-semaine), analysé par [Cronos](https://github.com/HangfireIO/Cronos). Évalué dans le fuseau horaire local de la machine du Server.

Voir [Contrôle d'accès](/fr/guide/architecture/access-control) pour le scope `messages:execute`, et [Messages](/fr/guide/sdk/messages) pour le fonctionnement des handlers de message.
