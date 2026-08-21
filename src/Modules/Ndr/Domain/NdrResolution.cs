namespace Courier.Ndr.Domain;

// Exact list from DOMAIN-MODEL.md's NDR entity. AutoRTO is a selectable resolution outcome only
// this phase (this plan's resolved design decision) — no scheduled/cron trigger exists to set it
// automatically; a human or a future automated caller must still call POST /ndrs/{id}/resolve.
public enum NdrResolution
{
    Reattempt,
    Rto,
    AutoRto,
    Other
}
