namespace PersonalFinance.Domain.Ledger.Enums
{
    public enum AccountType
    {
        Checking = 0,
        Savings = 1,
        Card = 2,
        Cash = 3
    }

    /// <summary>
    /// Transfer es dinero moviéndose entre cuentas propias (cuenta corriente a
    /// hucha, por ejemplo). No es ni gasto ni ingreso: cambia dónde está el
    /// dinero, no cuánto hay.
    /// </summary>
    public enum ConceptKind
    {
        Income = 0,
        Expense = 1,
        Transfer = 2
    }

    public enum ConceptNature
    {
        Fixed = 0,
        Variable = 1
    }

    public enum EntryDirection
    {
        In = 0,
        Out = 1
    }

    /// <summary>
    /// Planned: generado por una regla recurrente, aún no vencido.
    /// Pending: ya debería haber ocurrido y sigue sin confirmarse.
    /// Paid: confirmado, con importe real.
    /// Skipped: descartado para este mes sin romper la serie recurrente.
    /// </summary>
    public enum EntryStatus
    {
        Planned = 0,
        Pending = 1,
        Paid = 2,
        Skipped = 3
    }

    public enum PeriodStatus
    {
        Open = 0,
        Closed = 1
    }

    public enum RecurrenceFrequency
    {
        Monthly = 0,
        Quarterly = 1,
        Yearly = 2
    }

    public enum DebtKind
    {
        Loan = 0,
        CreditCard = 1,
        Mortgage = 2
    }

    public enum DebtStatus
    {
        Active = 0,
        Paid = 1,
        Defaulted = 2
    }

    public enum CounterpartyKind
    {
        Person = 0,
        Entity = 1
    }

    public enum PersonalLoanDirection
    {
        Given = 0,
        Received = 1
    }

    public enum ImportSource
    {
        RevolutCsv = 0,
        LegacyExcel = 1,
        Manual = 2
    }

    public enum ImportBatchStatus
    {
        Draft = 0,
        Reviewing = 1,
        Applied = 2,
        Discarded = 3
    }

    public enum ImportRowStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2,
        Duplicate = 3
    }
}
