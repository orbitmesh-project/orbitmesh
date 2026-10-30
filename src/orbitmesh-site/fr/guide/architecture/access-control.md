# Contrôle d'accès

Chaque connexion - Edge, package, tableau de bord, appel REST, ou une personne dans la Console - passe par la même courte chaîne de contrôles. Cette page montre d'abord la chaîne, puis chaque maillon en détail.

## Les couches en un coup d'œil

Une requête doit passer cinq contrôles, dans cet ordre. Chacun peut la refuser ; le premier refus l'arrête.

<div style="overflow-x:auto">
<svg viewBox="0 0 760 776" xmlns="http://www.w3.org/2000/svg" role="img" style="width:100%;max-width:760px;height:auto" aria-label="Une requête passe cinq contrôles dans l'ordre : réseau et verrouillage, authentification, porte, scope, puis règles d'autorisation. Chacun peut la refuser ; si aucun ne le fait, elle est autorisée.">
  <defs>
    <marker id="lay-fr-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-text-2)" />
    </marker>
    <marker id="lay-fr-stop" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-danger-1)" />
    </marker>
  </defs>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="20" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="44">1 · Réseau &amp; verrouillage</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="64">TLS et reverse proxy (à vous), TrustedProxies, CORS fermé par</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="81">défaut, verrouillage par IP : 10 échecs en 5 minutes bannissent</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="98">cette adresse 15 minutes.</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-fr-stop)" x1="500" y1="66" x2="528" y2="66" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="28" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="50">429 adresse verrouillée</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="66">Appel inter-origines bloqué</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="82">par le navigateur (CORS)</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-fr-arrow)" x1="265" y1="112" x2="265" y2="130" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="127">passe</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="132" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="156">2 · Authentification - qui êtes-vous ?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="176">Un AccessKey (identifiant Machine), ou le cookie de session de</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="193">quelqu'un connecté par mot de passe ou authentification unique.</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="210">Une mauvaise clé compte pour le verrouillage ; aucune clé, non.</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-fr-stop)" x1="500" y1="178" x2="528" y2="178" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="140" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="162">403 Forbidden</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="178">ou la connexion au hub</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="194">est fermée</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-fr-arrow)" x1="265" y1="224" x2="265" y2="242" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="239">passe</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="244" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="268">3 · Porte - quel point d'entrée ?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="288">API Management, hubs de la Console, Consumer, ou Edge / Package.</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="305">Une session n'ouvre que les portes de la Console ; la clé d'un</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="322">Edge ou d'un package n'ouvre que la porte qui lui est attribuée.</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-fr-stop)" x1="500" y1="290" x2="528" y2="290" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="252" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="274">403 mauvaise porte</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="290">une session ne peut se faire</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="306">passer pour un Edge</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-fr-arrow)" x1="265" y1="336" x2="265" y2="354" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="351">passe</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="356" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="380">4 · Scope - quel type d'action ?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="400">L'identité doit détenir le scope dont l'action a besoin :</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="417">credentials:manage, packages:control, edge:connect,</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="434">messages:execute...</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-fr-stop)" x1="500" y1="402" x2="528" y2="402" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="364" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="386">403 « requires the</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="402">&lt;scope&gt; scope »</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-fr-arrow)" x1="265" y1="448" x2="265" y2="466" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="463">passe</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="30" y="468" width="470" height="92" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="46" y="492">5 · Règles d'autorisation - sur quelle cible ?</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="512">Règles Allow / Deny de l'identifiant sur les messages, les groupes</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="529">de messages et les telemetry items (pas de règles pour les</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="546">utilisateurs SSO : seuls leurs scopes font barrière).</text>
  <line stroke="var(--vp-c-danger-1)" stroke-width="1.5" marker-end="url(#lay-fr-stop)" x1="500" y1="514" x2="528" y2="514" />
  <rect fill="var(--vp-c-danger-soft)" stroke="var(--vp-c-danger-1)" stroke-width="1.5" x="530" y="476" width="200" height="76" rx="8" />
  <text fill="var(--vp-c-danger-1)" font-size="11.5" font-weight="600" x="542" y="498">403 en REST</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="542" y="514">refusé ou ignoré sur les hubs</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#lay-fr-arrow)" x1="265" y1="560" x2="265" y2="578" />
  <text fill="var(--vp-c-text-3)" font-size="11" x="273" y="575">passe</text>
  <rect fill="var(--vp-c-green-soft)" stroke="var(--vp-c-green-1)" stroke-width="1.5" x="30" y="580" width="470" height="44" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="265" y="607" text-anchor="middle">Autorisée</text>
  <rect fill="none" stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x="30" y="644" width="700" height="112" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="13" font-weight="600" x="46" y="668">Les actions d'administration ajoutent des garde-fous</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="688">Délégation des scopes (on ne distribue que ceux qu'on détient), dernier identifiant non supprimable,</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="704">une modification de config ne peut pas retirer votre propre accès, secrets masqués en lecture,</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="720">et chaque appel Management qui modifie quelque chose est journalisé (catégorie Audit) avec</text>
  <text fill="var(--vp-c-text-2)" font-size="12" x="46" y="736">le nom de l'appelant.</text>
</svg>
</div>

| # | Couche | Question | Appliquée par | Réglable avec |
| --- | --- | --- | --- | --- |
| 1 | Réseau & verrouillage | Cette adresse a-t-elle le droit d'essayer ? | Votre TLS / reverse proxy, `LoginAttemptLimiter`, la politique CORS | `listenUrls`, `TrustedProxies`, `allowedOrigins` |
| 2 | Authentification | Qui est-ce ? | Vérification de l'AccessKey, ou du cookie de session | Page Credentials, options `Auth` |
| 3 | Porte | Est-ce le bon point d'entrée pour cette identité ? | Classe d'accès selon le chemin / le hub ; l'identifiant attribué à un Edge ou un package | Le `credential` d'un Edge et d'un package |
| 4 | Scope | A-t-elle le droit de faire ce type d'action ? | `RequiresScope` sur chaque endpoint Management, un contrôle dans chaque méthode de `ControlHub`, `edge:connect` / `package:connect` / `messages:execute` sur les autres hubs | Les `scopes` d'un identifiant, ou `roleMappings` |
| 5 | Règles d'autorisation | Sur cette cible précise ? | `Authorizations` de l'identifiant | Credentials → Authorizations |

Les couches 4 et 5 suivent le même modèle à deux niveaux qu'AWS IAM : un scope autorise une *classe d'action*, une règle restreint la *cible*.

## Qui peut utiliser quelle porte

| Porte | Utilisée par | Clé Machine | Session mot de passe | Session SSO |
| --- | --- | :---: | :---: | :---: |
| API Management (`/rest/management`) | La Console, des scripts | Oui - tout identifiant détenant au moins un scope, puis le scope propre à l'endpoint | Oui | Oui |
| `ControlHub` | La vue en direct de la Console | Oui (même règle) | Oui | Oui |
| `ConsumerHub`, `/rest/consumer` | Tableaux de bord, la Console | Oui - tout identifiant activé, puis scopes et règles | Oui | Oui |
| `EdgeHub` | Un process Edge | Seulement l'identifiant attribué à cet Edge, avec `edge:connect` | Non | Non |
| `OrbitMeshHub`, `/rest/orbitmesh` | Les packages | Seulement l'identifiant attribué à ce package (ou à son Edge), avec `package:connect` | Non | Non |
| L'edge `Developer` | Développement local | Une clé détenant `developer` | Non | Non |

Quelques portes sont ouvertes **volontairement**, chacune bornée : `/rest/enroll` (un Edge qui demande à être approuvé), `/rest/management/setup/*` (premier lancement uniquement), `/rest/auth/*` (connexion), les sites statiques sous un chemin `FileServers`, et `/metrics` (Prometheus). Ce que vous ne voulez pas public - `/metrics`, ou un site qui garde une clé dans son propre fichier de config - doit être derrière `localhostOnly`, votre reverse proxy ou un pare-feu.

## Trois sortes d'identité

| | Identifiant Machine | Identifiant Human | Utilisateur SSO |
| --- | --- | --- | --- |
| Pour | Edges, packages, tableaux de bord, scripts | Une personne avec un compte local | Une personne connectée via Authelia |
| Se prouve avec | Son AccessKey, à chaque requête ou connexion | Un mot de passe, une fois - puis une session | Le fournisseur d'identité, une fois - puis une session |
| Stocké sous forme | AES-256-GCM dans `appsettings.json` ; la clé est dans `keys/` | Un hash PBKDF2 salé | Rien - ses groupes arrivent avec la connexion |
| Dure | Jusqu'à réinitialisation, désactivation ou suppression | Session : 30 min d'inactivité, 24 h au plus | Idem |
| Ses droits viennent de | `scopes` + `authorizations` de l'identifiant | Idem, lus à chaud | `roleMappings` de ses groupes ; pas de règles par cible |
| Apparaît dans l'audit sous | Le nom de l'identifiant | Le nom de l'identifiant | `oidc:<login>` |

Une clé Machine n'est affichée qu'une fois, à la création ou à la réinitialisation. Elle authentifie un Edge, un package ou un script, mais ne peut jamais servir à se connecter à la Console.

## Scopes




Un identifiant porte une liste de **scopes** nommés (façon PAT, comme un token GitHub) : `edges:read`, `packages:manage`, `credentials:manage`, `updates:manage`, etc. Voir `OrbitMeshScope` pour la liste complète. Chaque scope couvre une tranche de la Management API/du `ControlHub` - ou, pour trois scopes ci-dessous, le protocole de base Edge/package/consumer lui-même.

L'identifiant propre à un Edge a besoin de `edge:connect` juste pour ouvrir sa connexion à `EdgeHub` ; l'identifiant propre à un package a besoin de `package:connect` pour ouvrir sa connexion à `OrbitMeshHub`, et de `messages:execute` pour envoyer un message via `SendMessage` (un identifiant Console/Consumer a besoin du même scope pour invoquer un message handler via `ConsumerHub`/l'endpoint REST `SendMessage`). Sans le scope correspondant, `Authorizations` ci-dessous n'est même pas consulté - un identifiant sans `edge:connect`/`package:connect` ne peut pas se connecter du tout, peu importe ce que dit `Authorizations`. L'identifiant d'un package reçoit les deux scopes automatiquement (`EnsurePackageCredential`, réappliqué à chaque déploiement ou changement de groupes) ; un Edge approuvé depuis la liste d'attente reçoit `edge:connect` de la même façon. Tous les autres scopes restent purement liés à l'administration.

## Règles d'autorisation




`authorizations` est une couche plus fine sous les scopes : des règles Allow/Deny par cible pour les messages, groupes et telemetry items. Même modèle à deux niveaux qu'AWS IAM - un scope autorise la classe d'action, une règle restreint la cible.

Chaque package reçoit son propre identifiant Machine auto-provisionné, au lieu de partager celui de son Edge. Par défaut `Deny` sur les trois, avec juste assez de règles `Allow` pour atteindre sa propre télémétrie edge+package, ses propres groupes configurés, et les messages adressés à lui-même :

```json
{
  "name": "salon.DayInfo",
  "accessKey": "ENC$...",
  "kind": "Machine",
  "scopes": ["package:connect", "messages:execute"],
  "authorizations": {
    "messages": {
      "defaultAuthorization": "Deny",
      "rules": [
        { "authorization": "Allow", "scope": "Package", "args": "DayInfo" },
        { "authorization": "Allow", "scope": "Edge", "args": "salon" }
      ]
    },
    "groups": { "defaultAuthorization": "Deny", "rules": [] },
    "telemetryItems": {
      "defaultAuthorization": "Deny",
      "rules": [{ "authorization": "Allow", "edgeName": "salon", "packageName": "DayInfo", "name": "*" }]
    }
  }
}
```

Atteindre la télémétrie d'un autre package, ou lui envoyer un message ciblé, nécessite une règle `Allow` explicite sur cet identifiant (Console → Credentials → Authorizations). La diffusion à tout le monde (portée de message `All`/`Others`) aussi.

## Se connecter à la Console




Les appelants machine (Edges, packages, tableaux de bord comme le kiosk) présentent leur `AccessKey` comme avant. Les **personnes**, non : la Console se connecte avec un nom d'utilisateur et un mot de passe et reçoit une *session* - un jeton opaque dans un cookie `HttpOnly`, `SameSite=Strict` que les scripts ne peuvent pas lire, conservé côté serveur sous forme de hash. Elle expire après 30 minutes d'inactivité (`OrbitMesh:Auth:SessionIdleMinutes`) et au plus après 24 heures (`SessionMaxHours`), se termine à la déconnexion, et est coupée si le compte est désactivé, supprimé ou voit son mot de passe réinitialisé. La Console ne fonctionne que depuis l'origine du Server lui-même.

Un compte créé par une version antérieure continue de fonctionner ; son hash est mis à niveau à la première connexion.

## Authentification unique avec Authelia (OpenID Connect)




À la place des comptes locaux (ou en plus), la Console peut connecter les gens via un fournisseur OpenID Connect. Ce qui suit est écrit pour [Authelia](https://www.authelia.com/) ; tout fournisseur gérant le flux « authorization code » avec PKCE et renvoyant une claim de groupes convient.

Déroulement de l'échange :

<div style="overflow-x:auto">
<svg viewBox="0 0 760 560" xmlns="http://www.w3.org/2000/svg" role="img" style="width:100%;max-width:760px;height:auto" aria-label="Séquence d'authentification unique : le navigateur demande à OrbitMesh de se connecter, est redirigé vers Authelia, s'y authentifie, revient avec un code, OrbitMesh l'échange contre l'identité et les groupes de l'utilisateur, mappe les groupes en scopes, puis pose un cookie de session.">
  <defs>
    <marker id="oidc-fr-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M0,0 L10,5 L0,10 z" fill="var(--vp-c-text-2)" />
    </marker>
  </defs>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="30" y="14" width="160" height="48" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="110" y="35" text-anchor="middle">Navigateur</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="110" y="52" text-anchor="middle">la Console</text>
  <line stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x1="110" y1="62" x2="110" y2="540" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="2" x="300" y="14" width="160" height="48" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="380" y="35" text-anchor="middle">OrbitMesh Server</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="380" y="52" text-anchor="middle">client confidentiel</text>
  <line stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x1="380" y1="62" x2="380" y2="540" />
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-divider)" stroke-width="1.5" x="570" y="14" width="160" height="48" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="14" font-weight="600" x="650" y="35" text-anchor="middle">Authelia</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="650" y="52" text-anchor="middle">fournisseur OpenID Connect</text>
  <line stroke="var(--vp-c-divider)" stroke-width="1.5" stroke-dasharray="5 4" x1="650" y1="62" x2="650" y2="540" />
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="112" y1="100" x2="376" y2="100" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="92" text-anchor="middle">① GET /rest/auth/oidc/login</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="378" y1="152" x2="114" y2="152" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="144" text-anchor="middle">② 302 vers le fournisseur (+ PKCE)</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="112" y1="204" x2="646" y2="204" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="380" y="196" text-anchor="middle">③ l'utilisateur s'y connecte (mot de passe, 2FA...)</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="648" y1="256" x2="114" y2="256" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="380" y="248" text-anchor="middle">④ 302 retour avec ?code=...</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="112" y1="308" x2="376" y2="308" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="300" text-anchor="middle">⑤ GET /rest/auth/oidc/callback?code=...</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="382" y1="360" x2="646" y2="360" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="515" y="352" text-anchor="middle">⑥ code + secret client + PKCE verifier</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" stroke-dasharray="5 4" marker-end="url(#oidc-fr-arrow)" x1="648" y1="412" x2="384" y2="412" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="515" y="404" text-anchor="middle">⑦ ID token, puis userinfo (groupes)</text>
  <rect fill="var(--vp-c-bg-soft)" stroke="var(--vp-c-brand-1)" stroke-width="1.5" x="205" y="442" width="350" height="56" rx="8" />
  <text fill="var(--vp-c-text-1)" font-size="12" font-weight="600" x="380" y="464" text-anchor="middle">⑧ groupes → scopes (role mappings)</text>
  <text fill="var(--vp-c-text-2)" font-size="11.5" x="380" y="481" text-anchor="middle">aucun groupe mappé = refusé, sinon une session démarre</text>
  <line stroke="var(--vp-c-text-2)" stroke-width="1.5" marker-end="url(#oidc-fr-arrow)" x1="378" y1="542" x2="114" y2="542" />
  <text fill="var(--vp-c-brand-1)" font-size="12" font-weight="600" x="245" y="534" text-anchor="middle">⑨ cookie de session + 302 vers la Console</text>
</svg>
</div>

**1. Déclarer OrbitMesh dans Authelia** (`configuration.yml` ; vérifiez les noms d'options dans la documentation de votre version d'Authelia) :

```yaml
identity_providers:
  oidc:
    clients:
      - client_id: orbitmesh
        client_name: OrbitMesh
        client_secret: '$pbkdf2-sha512$...'      # le hash du secret que vous mettez dans OrbitMesh ci-dessous
        public: false
        authorization_policy: two_factor
        redirect_uris:
          - https://orbitmesh.example.com/rest/auth/oidc/callback
        scopes: [openid, profile, email, groups]
        response_types: [code]
        grant_types: [authorization_code]
        require_pkce: true
        pkce_challenge_method: S256
        token_endpoint_auth_method: client_secret_post
```

**2. Configurer le Server.** Dans la Console, ouvrez **Administration → Single sign-on** : tous les réglages ci-dessous y sont, avec l'URI de redirection à déclarer chez le fournisseur, et le secret client reste masqué. Ou éditez `appsettings.json` (section `OrbitMesh`) à la main :

```json
"auth": {
  "oidc": {
    "enable": true,
    "displayName": "Authelia",
    "authority": "https://auth.example.com",
    "clientId": "orbitmesh",
    "clientSecret": "le-secret-en-clair",
    "roleMappings": [
      { "group": "orbitmesh-admins", "scopes": [ "*" ] },
      { "group": "orbitmesh-viewers", "scopes": [ "edges:read", "packages:read", "telemetry:read" ] }
    ]
  }
}
```

`displayName` est le nom affiché dans le bouton de connexion (« Sign in with Authelia » ; « SSO » si omis). `clientSecret` peut aussi venir de la variable d'environnement `OrbitMesh__Auth__Oidc__ClientSecret`. Redémarrez le Server après avoir modifié les réglages du fournisseur ; les correspondances de rôles, elles, s'appliquent tout de suite.

**Qui obtient quoi.** Un utilisateur reçoit l'union des scopes de chaque correspondance dont il est membre du `group` (`"*"` = tous les scopes) ; un utilisateur dans aucun d'eux est refusé. Les correspondances sont lues à chaud : en modifier une s'applique aux sessions déjà ouvertes. Ces utilisateurs apparaissent sous `oidc:<login>` dans le journal d'audit, n'ont pas d'entrée d'identifiant, et ne sont pas soumis aux règles `authorizations` par identifiant (les scopes font foi).

**À savoir.**

- Derrière un reverse proxy, conservez l'en-tête `Host` d'origine et listez le proxy dans `OrbitMesh:TrustedProxies` : l'URI de redirection envoyée à Authelia en est déduite, et doit correspondre exactement à `redirect_uris`.
- Gardez un compte admin local pour pouvoir entrer si Authelia est indisponible ; `auth.localLoginEnabled: false` supprime le formulaire de mot de passe.
- Avec l'authentification unique, il n'y a plus d'étape « créer l'admin » au premier lancement - les groupes décident.
- Ne placez pas en plus la Console derrière le forward-auth d'Authelia au niveau du proxy : les Edges, packages et tableaux de bord doivent atteindre `/signalr` et `/rest` avec leur AccessKey.

## Garde-fous de l'administration

- **Délégation des scopes.** `credentials:manage` et `configuration:write` ne valent pas root : on ne peut créer ou modifier un identifiant (ou un role mapping) que si l'on détient soi-même ses scopes, et on ne peut ni modifier ni supprimer un identifiant portant des scopes qu'on ne détient pas.
- **Le dernier identifiant ne peut pas être supprimé**, donc le Server ne peut pas être remis en état de premier lancement via l'API.
- **Pas d'auto-verrouillage via l'éditeur de configuration** : une modification qui retirerait votre propre `configuration:write` est refusée (y compris pour les utilisateurs SSO, jugés sur leurs groupes).
- **Les secrets sont masqués en lecture** (`********`) : clés et hashes d'identifiants, token de mise à jour, clés des flux NuGet, variables secrètes, réglages Password, secret client de l'authentification unique. Renvoyer le placeholder conserve la valeur stockée.
- **Clés en clair et chemins réservés refusés** par l'éditeur de configuration (`/rest`, `/signalr`, `/metrics` et `/packages` ne peuvent pas être détournés par un file server).
- **Tout est audité** : chaque appel Management qui modifie quelque chose, chaque connexion, refus et déconnexion va dans la catégorie de log `Audit`, avec le nom et l'adresse de l'appelant.

## Check-list de déploiement

- Servez en **HTTPS** (reverse proxy TLS ou Kestrel lui-même). Sans cela, AccessKeys et mots de passe circulent en clair et le cookie de session ne peut pas être `Secure`.
- Derrière un proxy, listez-le dans **`TrustedProxies`** et conservez l'en-tête `Host` d'origine : le verrouillage, `localhostOnly` et l'adresse de redirection de l'authentification unique en dépendent.
- Créez le **premier admin tout de suite** (ou configurez l'authentification unique, qui supprime cette étape).
- **Sauvegardez ensemble `appsettings.json` et le dossier `keys/`.** Sans `keys/credential-encryption.key`, les clés Machine ne peuvent plus être relues ; `keys/` contient aussi les sessions et l'état de l'authentification unique, gardez donc ses permissions strictes.
- Donnez à chaque identifiant Machine **uniquement les scopes dont il a besoin**, et resserrez ses `authorizations` (un tableau de bord qui affiche seulement de la télémétrie a besoin de `telemetry:read`, pas plus).
- **Restreignez `/metrics`** et ne mettez pas de secrets dans les sites statiques.
- Renseignez **`UpdateOptions:PublicKeys`** pour que les mises à jour soient vérifiées.

La liste complète de ce qui est protégé ou non, avec les limites connues, est dans [`SECURITY.md`](https://github.com/orbitmesh-project/orbitmesh/blob/main/SECURITY.md) ; la mise en place du HTTPS et du proxy est dans [Lancer Server & Edge](/fr/guide/installation/running#securiser-le-server).
