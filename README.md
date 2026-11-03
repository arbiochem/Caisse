# Caisse Réseau Madagascar — version .NET 8 MVC + SQL Server

Réécriture de l'application « Caisse Réseau Madagascar » (Next.js + Supabase) en **ASP.NET Core 8 MVC** avec
**SQL Server**, l'accès aux données se faisant en **LINQ (Entity Framework Core 8)**.
L'interface est inchangée : le fichier CSS d'origine (`wwwroot/css/site.css`) et le balisage (classes, structure,
libellés) ont été conservés à l'identique.

> ⚠️ **Ce projet n'a pas été compilé ni exécuté** : l'environnement où il a été écrit n'avait pas accès au SDK .NET
> ni à NuGet. Le code a été relu avec soin, mais il faut faire un premier `dotnet build` et corriger les éventuelles
> erreurs de compilation avant toute mise en service. Faites ensuite un passage de recette complet (voir plus bas).

## Prérequis
- SDK .NET 8
- SQL Server 2019+ (ou LocalDB / Express)

## Mise en route
1. Adapter la chaîne de connexion `ConnectionStrings:CaisseDb` dans `appsettings.json` (ou via les *user-secrets*).
2. Créer la base, au choix :
   - exécuter `Database/01_schema.sql` puis `Database/02_seed.sql` sur une base vide ; ou
   - mettre `"Database": { "AutoCreate": true }` : l'application crée le schéma au démarrage (`EnsureCreated`).
3. Créer le premier compte Direction : renseigner `Seed:AdminEmail` et `Seed:AdminPassword` (10 caractères minimum,
   idéalement via `dotnet user-secrets` ou des variables d'environnement `Seed__AdminEmail` / `Seed__AdminPassword`).
   Aucun mot de passe par défaut n'existe. Retirez ces valeurs une fois le compte créé.
4. `dotnet run --project CaisseReseau.Web`
5. Se connecter, puis ouvrir **Paramètres** (menu Administration) pour créer les comptes des agences et superviseurs.
6. Les adresses `xxx@a-configurer.invalid` des agences (table `agencies`, colonne `login_email`) sont des
   **valeurs fictives** : elles ne servent pas à la connexion, mais à votre référentiel.

## Mots de passe oubliés
Configurer la section `Smtp` (hôte, port, utilisateur, mot de passe, expéditeur). Sans SMTP, aucun lien n'est envoyé
(la Direction peut réinitialiser un mot de passe depuis **Paramètres**).

## Rôles (identiques à l'original)
| Rôle | Droits |
|---|---|
| `AGENCY` | lit et écrit sa propre agence |
| `SUPERVISOR` | lecture seule, toutes les agences |
| `SUPERVISOR_AGENCY` | écrit sa propre agence ; peut régulariser les autres si `can_regularize_closures` |
| `DIRECTION` | lecture, extraction, régularisation, gestion des comptes |

## Règles métier reprises (voir `Services/CashService.cs`)
- Ouverture : jour courant uniquement, à partir de 06 h 00 (heure de Madagascar, UTC+3) ; journée précédente fermée.
- Soldes par société : report = dernier comptage clôturé, plus fond de caisse optionnel ; relève de caissier journalisée.
- Clôture normale par société : 11 h 00 – 18 h 30 ; caisse théorique = report + fond + mouvements ESPÈCES ; un écart exige un commentaire.
- Fermeture du jour : toutes les sociétés actives doivent être clôturées.
- Régularisation tardive : motif d'au moins 5 caractères, droit spécial audité.
- Imputation SAGE : « À imputer » → « Imputé ».
- Acomptes : affectation = sortie `TRANSFERT_ACOMPTE` + entrée `REGLEMENT` sur la société cible, liées au ticket d'origine.
- Tickets : `PREFIXE-AGENCE-ANNÉE-NNNNNN`, séquence par agence / société / année garantie par `rowversion` + reprise sur conflit.
- Premier exemplaire imprimé à la création ; les réimpressions portent la mention « DUPLICATA ».
- Sorties FR : préfixe `[FR]` dans l'observation.

## Ce qui change par rapport à l'application d'origine
- **Supprimé** : file d'attente hors connexion (IndexedDB), baux de numérotation par poste, panneaux de synchronisation et de
  diagnostic. La saisie exige désormais une connexion au serveur ; la numérotation est attribuée par la base.
- **Remplacé** : Supabase Auth → comptes locaux (cookies, mots de passe PBKDF2-SHA256, lien de réinitialisation par e-mail) ;
  fonctions SQL / RLS → services C# + contrôles d'accès dans les contrôleurs.
- **Ajouté** : page *Paramètres* (gestion des comptes) pour la Direction.
- **Adapté** : le panneau « FR à contrôler » liste les sorties FR saisies (il lisait auparavant les résolutions de synchronisation).
- Textes touchés : la mention « Supabase » de l'écran de connexion et la ligne « Numérotation unique même hors connexion » du
  tableau de bord ont été reformulées ; rien d'autre n'a été modifié dans l'interface.

## Recette conseillée avant production
Compilation, connexion/déconnexion, ouverture de caisse (avec et sans fond), saisie de chaque mode de paiement,
impression + duplicata, imputation, affectation d'acompte, clôture par société (avec écart), fermeture du jour,
régularisation tardive, export Excel (ouverture des 12 onglets), accès par rôle (un compte agence ne voit que son agence).
