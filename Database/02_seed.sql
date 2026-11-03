/* Données de référence : sociétés et agences. Les adresses de connexion des agences sont des PLACEHOLDERS
   à remplacer par les vraies adresses. Les comptes utilisateurs se créent via la page Paramètres (Direction)
   ou via Seed:AdminEmail / Seed:AdminPassword au premier démarrage. */
SET NOCOUNT ON;

MERGE dbo.companies AS t
USING (VALUES
    (N'ARBIOCHEM',          N'ARBIOCHEM',                     N'ARB', 1),
    (N'ACTIVO',             N'ACTIVO',                        N'ACT', 2),
    (N'FEED',               N'FEED',                          N'FED', 3),
    (N'CAISSE_15',          N'Caisse 15',                     N'C15', 4),
    (N'AVANCE_POUSSIN',     N'Avance poussin',                N'AVP', 5),
    (N'VENTE_INTERNE',      N'Vente œuf et poulet en interne', N'VOI', 6),
    (N'VETCARE_CENTRALISE', N'Vetcare centralisé',            N'VTC', 7)
) AS s (code, display_name, ticket_prefix, sort_order)
ON t.code = s.code
WHEN NOT MATCHED THEN INSERT (code, display_name, ticket_prefix, active, sort_order) VALUES (s.code, s.display_name, s.ticket_prefix, 1, s.sort_order);

MERGE dbo.agencies AS t
USING (VALUES
    (N'AMB',  N'Ambohimangakely'),
    (N'ANA',  N'Analakely'),
    (N'ANT',  N'Antanimora'),
    (N'DIE',  N'Diégo'),
    (N'FAMB', N'FEED Ambohimangakely'),
    (N'IME',  N'Imerintsiatosika'),
    (N'MAH',  N'Mahitsy'),
    (N'MAJ',  N'Mahajanga'),
    (N'SAK',  N'Sakay'),
    (N'TAM',  N'Tamatave'),
    (N'TOL',  N'Toliara'),
    (N'VAM',  N'Vetcare Ambatomitsangana'),
    (N'VIV',  N'Vetcare Ivato')
) AS s (code, name)
ON t.code = s.code
WHEN NOT MATCHED THEN INSERT (code, name, login_email, active) VALUES (s.code, s.name, LOWER(s.code) + N'@a-configurer.invalid', 1);
GO
