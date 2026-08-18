using Courier.Geography.Domain;

namespace Courier.Geography.Infrastructure;

// Real Nepal geography reference data, scoped per this phase's approved seed plan (2026-08-18):
// full provinces/districts (stable, small, genuinely useful everywhere), partial local
// levels/wards/areas limited to the 5 districts CLAUDE.md §1's example org operates in. Full
// 753-local-level national coverage is a follow-up data-loading task, not application logic.
public static class GeographySeedData
{
    public static readonly string[] Provinces =
    {
        "Koshi", "Madhesh", "Bagmati", "Gandaki", "Lumbini", "Karnali", "Sudurpashchim"
    };

    // (Province, District) — all 77 districts, official as of the 2015 federal restructuring.
    public static readonly (string Province, string District)[] Districts =
    {
        ("Koshi", "Bhojpur"), ("Koshi", "Dhankuta"), ("Koshi", "Ilam"), ("Koshi", "Jhapa"),
        ("Koshi", "Khotang"), ("Koshi", "Morang"), ("Koshi", "Okhaldhunga"), ("Koshi", "Panchthar"),
        ("Koshi", "Sankhuwasabha"), ("Koshi", "Solukhumbu"), ("Koshi", "Sunsari"), ("Koshi", "Taplejung"),
        ("Koshi", "Terhathum"), ("Koshi", "Udayapur"),

        ("Madhesh", "Bara"), ("Madhesh", "Dhanusha"), ("Madhesh", "Mahottari"), ("Madhesh", "Parsa"),
        ("Madhesh", "Rautahat"), ("Madhesh", "Saptari"), ("Madhesh", "Sarlahi"), ("Madhesh", "Siraha"),

        ("Bagmati", "Bhaktapur"), ("Bagmati", "Chitwan"), ("Bagmati", "Dhading"), ("Bagmati", "Dolakha"),
        ("Bagmati", "Kathmandu"), ("Bagmati", "Kavrepalanchok"), ("Bagmati", "Lalitpur"), ("Bagmati", "Makwanpur"),
        ("Bagmati", "Nuwakot"), ("Bagmati", "Ramechhap"), ("Bagmati", "Rasuwa"), ("Bagmati", "Sindhuli"),
        ("Bagmati", "Sindhupalchok"),

        ("Gandaki", "Baglung"), ("Gandaki", "Gorkha"), ("Gandaki", "Kaski"), ("Gandaki", "Lamjung"),
        ("Gandaki", "Manang"), ("Gandaki", "Mustang"), ("Gandaki", "Myagdi"), ("Gandaki", "Nawalpur"),
        ("Gandaki", "Parbat"), ("Gandaki", "Syangja"), ("Gandaki", "Tanahun"),

        ("Lumbini", "Arghakhanchi"), ("Lumbini", "Banke"), ("Lumbini", "Bardiya"), ("Lumbini", "Dang"),
        ("Lumbini", "Eastern Rukum"), ("Lumbini", "Gulmi"), ("Lumbini", "Kapilvastu"), ("Lumbini", "Parasi"),
        ("Lumbini", "Palpa"), ("Lumbini", "Pyuthan"), ("Lumbini", "Rolpa"), ("Lumbini", "Rupandehi"),

        ("Karnali", "Dailekh"), ("Karnali", "Dolpa"), ("Karnali", "Humla"), ("Karnali", "Jajarkot"),
        ("Karnali", "Jumla"), ("Karnali", "Kalikot"), ("Karnali", "Mugu"), ("Karnali", "Salyan"),
        ("Karnali", "Surkhet"), ("Karnali", "Western Rukum"),

        ("Sudurpashchim", "Achham"), ("Sudurpashchim", "Baitadi"), ("Sudurpashchim", "Bajhang"),
        ("Sudurpashchim", "Bajura"), ("Sudurpashchim", "Dadeldhura"), ("Sudurpashchim", "Darchula"),
        ("Sudurpashchim", "Doti"), ("Sudurpashchim", "Kailali"), ("Sudurpashchim", "Kanchanpur"),
    };

    // (Province, District, LocalLevel, Type) — one representative urban local level per district,
    // for the 5 districts CLAUDE.md §1's example org (Head Office/Kathmandu Hub/Lalitpur
    // Branch/Bhaktapur Branch/Pokhara Hub/Butwal Branch) actually operates in.
    public static readonly (string Province, string District, string LocalLevel, LocalLevelType Type)[] LocalLevels =
    {
        ("Bagmati", "Kathmandu", "Kathmandu Metropolitan City", LocalLevelType.Metropolitan),
        ("Bagmati", "Lalitpur", "Lalitpur Metropolitan City", LocalLevelType.Metropolitan),
        ("Bagmati", "Bhaktapur", "Bhaktapur Municipality", LocalLevelType.Municipality),
        ("Gandaki", "Kaski", "Pokhara Metropolitan City", LocalLevelType.Metropolitan),
        ("Lumbini", "Rupandehi", "Butwal Sub-Metropolitan City", LocalLevelType.SubMetropolitan),
    };

    // Ward numbers 1-3 seeded for each of the 5 local levels above (every one of them genuinely
    // has at least wards 1-3, so this is real data, just not the full ward count).
    public static readonly int[] WardNumbers = { 1, 2, 3 };

    // Area labels seeded per ward — representative test/demo coverage granularity, not claimed
    // as authoritative neighborhood-boundary data.
    public static readonly string[] AreaLabels = { "Area A", "Area B" };
}
