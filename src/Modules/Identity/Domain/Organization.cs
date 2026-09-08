namespace Courier.Identity.Domain;

public class Organization
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;
    // True only when the overdue-invoice job (Billing Task 13) auto-suspended this org for
    // non-payment — distinguishes that from a SuperAdmin's manual suspend (Task 9) so a later
    // successful payment (Task 6) knows it is safe to auto-reactivate. A manual suspend/
    // reactivate always leaves or resets this to false (Task 9).
    public bool SuspendedBySystem { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
