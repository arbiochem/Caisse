/* Caisse Réseau Madagascar — schéma SQL Server (équivalent du modèle EF Core de CaisseReseau.Web).
   À exécuter sur une base vide, ou laisser l'application créer le schéma avec "Database:AutoCreate": true. */
SET NOCOUNT ON;
GO

CREATE TABLE dbo.agencies (
    code            NVARCHAR(20)  NOT NULL CONSTRAINT PK_agencies PRIMARY KEY,
    name            NVARCHAR(100) NOT NULL,
    login_email     NVARCHAR(200) NOT NULL,
    active          BIT           NOT NULL CONSTRAINT DF_agencies_active DEFAULT 1,
    created_at      DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_agencies_created DEFAULT SYSDATETIMEOFFSET()
);
CREATE UNIQUE INDEX IX_agencies_login_email ON dbo.agencies (login_email);

CREATE TABLE dbo.companies (
    code            NVARCHAR(40)  NOT NULL CONSTRAINT PK_companies PRIMARY KEY,
    display_name    NVARCHAR(100) NOT NULL,
    ticket_prefix   NVARCHAR(10)  NOT NULL,
    active          BIT           NOT NULL CONSTRAINT DF_companies_active DEFAULT 1,
    sort_order      INT           NOT NULL CONSTRAINT DF_companies_sort DEFAULT 0,
    created_at      DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_companies_created DEFAULT SYSDATETIMEOFFSET()
);

CREATE TABLE dbo.account_access (
    email                                  NVARCHAR(200) NOT NULL CONSTRAINT PK_account_access PRIMARY KEY,
    role                                   NVARCHAR(30)  NOT NULL,
    agency_code                            NVARCHAR(20)  NULL,
    active                                 BIT           NOT NULL CONSTRAINT DF_account_active DEFAULT 1,
    can_regularize_closures                BIT           NOT NULL CONSTRAINT DF_account_regul DEFAULT 0,
    regularization_excluded_agency_code    NVARCHAR(20)  NULL,
    password_hash                          NVARCHAR(300) NULL,
    reset_token_hash                       NVARCHAR(100) NULL,
    reset_token_expires_at                 DATETIMEOFFSET(7) NULL,
    created_at                             DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_account_created DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT CK_account_role CHECK (role IN ('AGENCY','SUPERVISOR','SUPERVISOR_AGENCY','DIRECTION')),
    CONSTRAINT FK_account_agency FOREIGN KEY (agency_code) REFERENCES dbo.agencies (code),
    CONSTRAINT FK_account_excluded_agency FOREIGN KEY (regularization_excluded_agency_code) REFERENCES dbo.agencies (code)
);

CREATE TABLE dbo.cash_sessions (
    id                    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cash_sessions PRIMARY KEY,
    agency_code           NVARCHAR(20)  NOT NULL,
    business_date         DATE          NOT NULL,
    cashier_name          NVARCHAR(100) NOT NULL,
    opening_balance       BIGINT        NOT NULL,
    source_closure_date   DATE          NULL,
    started_by            NVARCHAR(200) NOT NULL,
    started_at            DATETIMEOFFSET(7) NOT NULL,
    updated_by            NVARCHAR(200) NOT NULL,
    updated_at            DATETIMEOFFSET(7) NOT NULL,
    status                NVARCHAR(10)  NOT NULL,
    closed_at             DATETIMEOFFSET(7) NULL,
    closed_by             NVARCHAR(200) NULL,
    closed_late           BIT           NOT NULL CONSTRAINT DF_sessions_late DEFAULT 0,
    late_closure_reason   NVARCHAR(400) NULL,
    regularized_at        DATETIMEOFFSET(7) NULL,
    regularized_by        NVARCHAR(200) NULL,
    CONSTRAINT CK_sessions_status CHECK (status IN ('OPEN','CLOSED')),
    CONSTRAINT FK_sessions_agency FOREIGN KEY (agency_code) REFERENCES dbo.agencies (code)
);
CREATE UNIQUE INDEX IX_cash_sessions_agency_date ON dbo.cash_sessions (agency_code, business_date);

CREATE TABLE dbo.cash_session_company_balances (
    cash_session_id       BIGINT        NOT NULL,
    company               NVARCHAR(40)  NOT NULL,
    agency_code           NVARCHAR(20)  NOT NULL,
    business_date         DATE          NOT NULL,
    carried_balance       BIGINT        NOT NULL,
    cash_fund             BIGINT        NOT NULL,
    source_closure_date   DATE          NULL,
    created_at            DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT PK_session_balances PRIMARY KEY (cash_session_id, company),
    CONSTRAINT FK_balances_session FOREIGN KEY (cash_session_id) REFERENCES dbo.cash_sessions (id) ON DELETE CASCADE
);

CREATE TABLE dbo.cashier_shifts (
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cashier_shifts PRIMARY KEY,
    cash_session_id  BIGINT        NOT NULL,
    agency_code      NVARCHAR(20)  NOT NULL,
    business_date    DATE          NOT NULL,
    cashier_name     NVARCHAR(100) NOT NULL,
    action           NVARCHAR(20)  NOT NULL,
    recorded_by      NVARCHAR(200) NOT NULL,
    recorded_at      DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_shifts_session FOREIGN KEY (cash_session_id) REFERENCES dbo.cash_sessions (id) ON DELETE CASCADE
);
CREATE INDEX IX_cashier_shifts_cash_session_id ON dbo.cashier_shifts (cash_session_id);

CREATE TABLE dbo.cash_operations (
    id                       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cash_operations PRIMARY KEY,
    cash_session_id          BIGINT        NOT NULL,
    agency_code              NVARCHAR(20)  NOT NULL,
    agency_name              NVARCHAR(100) NOT NULL,
    company                  NVARCHAR(40)  NOT NULL,
    channel                  NVARCHAR(40)  NOT NULL,
    direction                NVARCHAR(10)  NOT NULL,
    transaction_date         DATE          NOT NULL,
    ticket_number            NVARCHAR(120) NOT NULL,
    invoice_number           NVARCHAR(120) NULL,
    customer_name            NVARCHAR(160) NOT NULL,
    amount                   BIGINT        NOT NULL,
    reference                NVARCHAR(160) NULL,
    bank_name                NVARCHAR(80)  NULL,
    document_number          NVARCHAR(80)  NULL,
    maturity_date            DATE          NULL,
    sage_status              NVARCHAR(20)  NOT NULL,
    notes                    NVARCHAR(400) NULL,
    cashier_name             NVARCHAR(100) NOT NULL,
    print_count              INT           NOT NULL,
    last_printed_at          DATETIMEOFFSET(7) NULL,
    last_printed_by          NVARCHAR(200) NULL,
    created_by               NVARCHAR(200) NOT NULL,
    created_at               DATETIMEOFFSET(7) NOT NULL,
    transaction_nature       NVARCHAR(40)  NOT NULL,
    allocated_amount         BIGINT        NOT NULL CONSTRAINT DF_ops_allocated DEFAULT 0,
    source_deposit_id        BIGINT        NULL,
    sage_status_updated_at   DATETIMEOFFSET(7) NULL,
    sage_status_updated_by   NVARCHAR(200) NULL,
    client_operation_id      NVARCHAR(100) NULL,
    CONSTRAINT CK_ops_amount CHECK (amount > 0),
    CONSTRAINT CK_ops_direction CHECK (direction IN ('ENTREE','SORTIE')),
    CONSTRAINT FK_ops_session FOREIGN KEY (cash_session_id) REFERENCES dbo.cash_sessions (id)
);
CREATE UNIQUE INDEX IX_cash_operations_ticket_number ON dbo.cash_operations (ticket_number);
CREATE UNIQUE INDEX IX_cash_operations_client_operation_id ON dbo.cash_operations (client_operation_id) WHERE client_operation_id IS NOT NULL;
CREATE INDEX IX_cash_operations_agency_date ON dbo.cash_operations (agency_code, transaction_date);
CREATE INDEX IX_cash_operations_cash_session_id ON dbo.cash_operations (cash_session_id);

CREATE TABLE dbo.company_daily_closures (
    id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_company_daily_closures PRIMARY KEY,
    agency_code       NVARCHAR(20)  NOT NULL,
    agency_name       NVARCHAR(100) NOT NULL,
    company           NVARCHAR(40)  NOT NULL,
    closure_date      DATE          NOT NULL,
    opening_cash      BIGINT        NOT NULL,
    cash_fund         BIGINT        NOT NULL,
    cash_theoretical  BIGINT        NOT NULL,
    cash_counted      BIGINT        NOT NULL,
    variance          BIGINT        NOT NULL,
    breakdown         NVARCHAR(MAX) NOT NULL,
    comment           NVARCHAR(500) NULL,
    cashier_name      NVARCHAR(100) NOT NULL,
    status            NVARCHAR(30)  NOT NULL,
    submitted_by      NVARCHAR(200) NOT NULL,
    submitted_at      DATETIMEOFFSET(7) NOT NULL,
    is_late           BIT           NOT NULL CONSTRAINT DF_closures_late DEFAULT 0,
    late_reason       NVARCHAR(400) NULL,
    regularized_at    DATETIMEOFFSET(7) NULL,
    regularized_by    NVARCHAR(200) NULL
);
CREATE UNIQUE INDEX IX_closures_agency_company_date ON dbo.company_daily_closures (agency_code, company, closure_date);

CREATE TABLE dbo.deposit_allocations (
    id                         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_deposit_allocations PRIMARY KEY,
    source_operation_id        BIGINT        NOT NULL,
    transfer_out_operation_id  BIGINT        NOT NULL,
    transfer_in_operation_id   BIGINT        NOT NULL,
    agency_code                NVARCHAR(20)  NOT NULL,
    source_company             NVARCHAR(40)  NOT NULL,
    target_company             NVARCHAR(40)  NOT NULL,
    allocated_amount           BIGINT        NOT NULL,
    invoice_number             NVARCHAR(120) NOT NULL,
    allocation_date            DATE          NOT NULL,
    created_by                 NVARCHAR(200) NOT NULL,
    created_at                 DATETIMEOFFSET(7) NOT NULL,
    client_allocation_id       NVARCHAR(100) NULL,
    CONSTRAINT FK_alloc_source FOREIGN KEY (source_operation_id) REFERENCES dbo.cash_operations (id),
    CONSTRAINT FK_alloc_out FOREIGN KEY (transfer_out_operation_id) REFERENCES dbo.cash_operations (id),
    CONSTRAINT FK_alloc_in FOREIGN KEY (transfer_in_operation_id) REFERENCES dbo.cash_operations (id)
);
CREATE UNIQUE INDEX IX_deposit_allocations_client_id ON dbo.deposit_allocations (client_allocation_id) WHERE client_allocation_id IS NOT NULL;

CREATE TABLE dbo.ticket_print_events (
    id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ticket_print_events PRIMARY KEY,
    operation_id     BIGINT        NOT NULL,
    client_print_id  NVARCHAR(100) NOT NULL,
    printed_by       NVARCHAR(200) NOT NULL,
    printed_at       DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT FK_print_operation FOREIGN KEY (operation_id) REFERENCES dbo.cash_operations (id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX IX_ticket_print_events_client_print_id ON dbo.ticket_print_events (client_print_id);
CREATE INDEX IX_ticket_print_events_operation_id ON dbo.ticket_print_events (operation_id);

CREATE TABLE dbo.ticket_sequences (
    agency_code    NVARCHAR(20) NOT NULL,
    company        NVARCHAR(40) NOT NULL,
    calendar_year  INT          NOT NULL,
    last_number    BIGINT       NOT NULL,
    updated_at     DATETIMEOFFSET(7) NOT NULL,
    row_version    ROWVERSION   NOT NULL,
    CONSTRAINT PK_ticket_sequences PRIMARY KEY (agency_code, company, calendar_year)
);
GO
