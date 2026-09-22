namespace Courier.Geography.Infrastructure;

// ISO-3166-1 alpha-2 code, English name, default currency (ISO-4217 format, not membership-
// checked — see SharedKernel.CurrencyCode), calling code. Covers Nepal, every UN member state,
// plus Vatican City, the State of Palestine, Kosovo and Taiwan, plus a set of dependent
// territories/customs areas relevant to international shipping (Hong Kong, Macao, Puerto Rico,
// Greenland, Bermuda, Cayman Islands, Gibraltar, Isle of Man, Jersey, Guernsey, Faroe Islands,
// Guam, American Samoa, US/British Virgin Islands, Aruba, Curaçao, New Caledonia, French
// Polynesia). This is NOT the complete ISO-3166-1 alpha-2 list — uninhabited/unshippable entries
// (Antarctica, Bouvet Island, South Georgia, etc.) are deliberately excluded. More rows can be
// added later via an additive migration; Country has no create API in I2.
public static class CountrySeedData
{
    public static readonly (string IsoCode, string NameEn, string DefaultCurrency, string CallingCode)[] Countries =
    {
        ("NP", "Nepal", "NPR", "+977"),

        // Africa
        ("DZ", "Algeria", "DZD", "+213"), ("AO", "Angola", "AOA", "+244"), ("BJ", "Benin", "XOF", "+229"),
        ("BW", "Botswana", "BWP", "+267"), ("BF", "Burkina Faso", "XOF", "+226"), ("BI", "Burundi", "BIF", "+257"),
        ("CV", "Cabo Verde", "CVE", "+238"), ("CM", "Cameroon", "XAF", "+237"), ("CF", "Central African Republic", "XAF", "+236"),
        ("TD", "Chad", "XAF", "+235"), ("KM", "Comoros", "KMF", "+269"), ("CG", "Congo", "XAF", "+242"),
        ("CD", "Congo (DRC)", "CDF", "+243"), ("CI", "Côte d'Ivoire", "XOF", "+225"), ("DJ", "Djibouti", "DJF", "+253"),
        ("EG", "Egypt", "EGP", "+20"), ("GQ", "Equatorial Guinea", "XAF", "+240"), ("ER", "Eritrea", "ERN", "+291"),
        ("SZ", "Eswatini", "SZL", "+268"), ("ET", "Ethiopia", "ETB", "+251"), ("GA", "Gabon", "XAF", "+241"),
        ("GM", "Gambia", "GMD", "+220"), ("GH", "Ghana", "GHS", "+233"), ("GN", "Guinea", "GNF", "+224"),
        ("GW", "Guinea-Bissau", "XOF", "+245"), ("KE", "Kenya", "KES", "+254"), ("LS", "Lesotho", "LSL", "+266"),
        ("LR", "Liberia", "LRD", "+231"), ("LY", "Libya", "LYD", "+218"), ("MG", "Madagascar", "MGA", "+261"),
        ("MW", "Malawi", "MWK", "+265"), ("ML", "Mali", "XOF", "+223"), ("MR", "Mauritania", "MRU", "+222"),
        ("MU", "Mauritius", "MUR", "+230"), ("MA", "Morocco", "MAD", "+212"), ("MZ", "Mozambique", "MZN", "+258"),
        ("NA", "Namibia", "NAD", "+264"), ("NE", "Niger", "XOF", "+227"), ("NG", "Nigeria", "NGN", "+234"),
        ("RW", "Rwanda", "RWF", "+250"), ("ST", "Sao Tome and Principe", "STN", "+239"), ("SN", "Senegal", "XOF", "+221"),
        ("SC", "Seychelles", "SCR", "+248"), ("SL", "Sierra Leone", "SLE", "+232"), ("SO", "Somalia", "SOS", "+252"),
        ("ZA", "South Africa", "ZAR", "+27"), ("SS", "South Sudan", "SSP", "+211"), ("SD", "Sudan", "SDG", "+249"),
        ("TZ", "Tanzania", "TZS", "+255"), ("TG", "Togo", "XOF", "+228"), ("TN", "Tunisia", "TND", "+216"),
        ("UG", "Uganda", "UGX", "+256"), ("ZM", "Zambia", "ZMW", "+260"), ("ZW", "Zimbabwe", "ZWL", "+263"),
        ("EH", "Western Sahara", "MAD", "+212"),

        // Americas
        ("AG", "Antigua and Barbuda", "XCD", "+1"), ("AR", "Argentina", "ARS", "+54"), ("BS", "Bahamas", "BSD", "+1"),
        ("BB", "Barbados", "BBD", "+1"), ("BZ", "Belize", "BZD", "+501"), ("BO", "Bolivia", "BOB", "+591"),
        ("BR", "Brazil", "BRL", "+55"), ("CA", "Canada", "CAD", "+1"), ("CL", "Chile", "CLP", "+56"),
        ("CO", "Colombia", "COP", "+57"), ("CR", "Costa Rica", "CRC", "+506"), ("CU", "Cuba", "CUP", "+53"),
        ("DM", "Dominica", "XCD", "+1"), ("DO", "Dominican Republic", "DOP", "+1"), ("EC", "Ecuador", "USD", "+593"),
        ("SV", "El Salvador", "USD", "+503"), ("GD", "Grenada", "XCD", "+1"), ("GT", "Guatemala", "GTQ", "+502"),
        ("GY", "Guyana", "GYD", "+592"), ("HT", "Haiti", "HTG", "+509"), ("HN", "Honduras", "HNL", "+504"),
        ("JM", "Jamaica", "JMD", "+1"), ("MX", "Mexico", "MXN", "+52"), ("NI", "Nicaragua", "NIO", "+505"),
        ("PA", "Panama", "PAB", "+507"), ("PY", "Paraguay", "PYG", "+595"), ("PE", "Peru", "PEN", "+51"),
        ("KN", "Saint Kitts and Nevis", "XCD", "+1"), ("LC", "Saint Lucia", "XCD", "+1"),
        ("VC", "Saint Vincent and the Grenadines", "XCD", "+1"), ("SR", "Suriname", "SRD", "+597"),
        ("TT", "Trinidad and Tobago", "TTD", "+1"), ("US", "United States", "USD", "+1"), ("UY", "Uruguay", "UYU", "+598"),
        ("VE", "Venezuela", "VES", "+58"),

        // Asia
        ("AF", "Afghanistan", "AFN", "+93"), ("AM", "Armenia", "AMD", "+374"), ("AZ", "Azerbaijan", "AZN", "+994"),
        ("BH", "Bahrain", "BHD", "+973"), ("BD", "Bangladesh", "BDT", "+880"), ("BT", "Bhutan", "BTN", "+975"),
        ("BN", "Brunei", "BND", "+673"), ("KH", "Cambodia", "KHR", "+855"), ("CN", "China", "CNY", "+86"),
        ("CY", "Cyprus", "EUR", "+357"), ("GE", "Georgia", "GEL", "+995"), ("IN", "India", "INR", "+91"),
        ("ID", "Indonesia", "IDR", "+62"), ("IR", "Iran", "IRR", "+98"), ("IQ", "Iraq", "IQD", "+964"),
        ("IL", "Israel", "ILS", "+972"), ("JP", "Japan", "JPY", "+81"), ("JO", "Jordan", "JOD", "+962"),
        ("KZ", "Kazakhstan", "KZT", "+7"), ("KW", "Kuwait", "KWD", "+965"), ("KG", "Kyrgyzstan", "KGS", "+996"),
        ("LA", "Laos", "LAK", "+856"), ("LB", "Lebanon", "LBP", "+961"), ("MY", "Malaysia", "MYR", "+60"),
        ("MV", "Maldives", "MVR", "+960"), ("MN", "Mongolia", "MNT", "+976"), ("MM", "Myanmar", "MMK", "+95"),
        ("KP", "North Korea", "KPW", "+850"), ("OM", "Oman", "OMR", "+968"), ("PK", "Pakistan", "PKR", "+92"),
        ("PS", "Palestine", "ILS", "+970"), ("PH", "Philippines", "PHP", "+63"), ("QA", "Qatar", "QAR", "+974"),
        ("SA", "Saudi Arabia", "SAR", "+966"), ("SG", "Singapore", "SGD", "+65"), ("KR", "South Korea", "KRW", "+82"),
        ("LK", "Sri Lanka", "LKR", "+94"), ("SY", "Syria", "SYP", "+963"), ("TW", "Taiwan", "TWD", "+886"),
        ("TJ", "Tajikistan", "TJS", "+992"), ("TH", "Thailand", "THB", "+66"), ("TL", "Timor-Leste", "USD", "+670"),
        ("TR", "Turkey", "TRY", "+90"), ("TM", "Turkmenistan", "TMT", "+993"), ("AE", "United Arab Emirates", "AED", "+971"),
        ("UZ", "Uzbekistan", "UZS", "+998"), ("VN", "Vietnam", "VND", "+84"), ("YE", "Yemen", "YER", "+967"),

        // Europe
        ("AL", "Albania", "ALL", "+355"), ("AD", "Andorra", "EUR", "+376"), ("AT", "Austria", "EUR", "+43"),
        ("BY", "Belarus", "BYN", "+375"), ("BE", "Belgium", "EUR", "+32"), ("BA", "Bosnia and Herzegovina", "BAM", "+387"),
        ("BG", "Bulgaria", "BGN", "+359"), ("HR", "Croatia", "EUR", "+385"), ("CZ", "Czechia", "CZK", "+420"),
        ("DK", "Denmark", "DKK", "+45"), ("EE", "Estonia", "EUR", "+372"), ("FI", "Finland", "EUR", "+358"),
        ("FR", "France", "EUR", "+33"), ("DE", "Germany", "EUR", "+49"), ("GR", "Greece", "EUR", "+30"),
        ("HU", "Hungary", "HUF", "+36"), ("IS", "Iceland", "ISK", "+354"), ("IE", "Ireland", "EUR", "+353"),
        ("IT", "Italy", "EUR", "+39"), ("XK", "Kosovo", "EUR", "+383"), ("LV", "Latvia", "EUR", "+371"),
        ("LI", "Liechtenstein", "CHF", "+423"), ("LT", "Lithuania", "EUR", "+370"), ("LU", "Luxembourg", "EUR", "+352"),
        ("MT", "Malta", "EUR", "+356"), ("MD", "Moldova", "MDL", "+373"), ("MC", "Monaco", "EUR", "+377"),
        ("ME", "Montenegro", "EUR", "+382"), ("NL", "Netherlands", "EUR", "+31"), ("MK", "North Macedonia", "MKD", "+389"),
        ("NO", "Norway", "NOK", "+47"), ("PL", "Poland", "PLN", "+48"), ("PT", "Portugal", "EUR", "+351"),
        ("RO", "Romania", "RON", "+40"), ("RU", "Russia", "RUB", "+7"), ("SM", "San Marino", "EUR", "+378"),
        ("RS", "Serbia", "RSD", "+381"), ("SK", "Slovakia", "EUR", "+421"), ("SI", "Slovenia", "EUR", "+386"),
        ("ES", "Spain", "EUR", "+34"), ("SE", "Sweden", "SEK", "+46"), ("CH", "Switzerland", "CHF", "+41"),
        ("UA", "Ukraine", "UAH", "+380"), ("GB", "United Kingdom", "GBP", "+44"), ("VA", "Vatican City", "EUR", "+379"),

        // Oceania
        ("AU", "Australia", "AUD", "+61"), ("FJ", "Fiji", "FJD", "+679"), ("KI", "Kiribati", "AUD", "+686"),
        ("MH", "Marshall Islands", "USD", "+692"), ("FM", "Micronesia", "USD", "+691"), ("NR", "Nauru", "AUD", "+674"),
        ("NZ", "New Zealand", "NZD", "+64"), ("PW", "Palau", "USD", "+680"), ("PG", "Papua New Guinea", "PGK", "+675"),
        ("WS", "Samoa", "WST", "+685"), ("SB", "Solomon Islands", "SBD", "+677"), ("TO", "Tonga", "TOP", "+676"),
        ("TV", "Tuvalu", "AUD", "+688"), ("VU", "Vanuatu", "VUV", "+678"),

        // Dependent territories / customs areas relevant to international shipping
        ("HK", "Hong Kong", "HKD", "+852"), ("MO", "Macao", "MOP", "+853"), ("PR", "Puerto Rico", "USD", "+1"),
        ("GL", "Greenland", "DKK", "+299"), ("BM", "Bermuda", "BMD", "+1"), ("KY", "Cayman Islands", "KYD", "+1"),
        ("GI", "Gibraltar", "GIP", "+350"), ("IM", "Isle of Man", "GBP", "+44"), ("JE", "Jersey", "GBP", "+44"),
        ("GG", "Guernsey", "GBP", "+44"), ("FO", "Faroe Islands", "DKK", "+298"), ("GU", "Guam", "USD", "+1"),
        ("AS", "American Samoa", "USD", "+1"), ("VI", "United States Virgin Islands", "USD", "+1"),
        ("VG", "British Virgin Islands", "USD", "+1"), ("AW", "Aruba", "AWG", "+297"), ("CW", "Curaçao", "ANG", "+599"),
        ("NC", "New Caledonia", "XPF", "+687"), ("PF", "French Polynesia", "XPF", "+689"),
    };

    // Nepali names for Nepal's most likely early trading-partner destinations (approved 2026-09-22
    // brainstorm decision). Every other row's NameNe is null; the UI falls back to NameEn.
    public static readonly IReadOnlyDictionary<string, string> NameNe = new Dictionary<string, string>
    {
        ["IN"] = "भारत",
        ["CN"] = "चीन",
        ["US"] = "संयुक्त राज्य अमेरिका",
        ["GB"] = "संयुक्त अधिराज्य",
        ["AE"] = "संयुक्त अरब इमिरेट्स",
        ["QA"] = "कतार",
        ["AU"] = "अस्ट्रेलिया",
        ["CA"] = "क्यानाडा",
        ["MY"] = "मलेसिया",
        ["KR"] = "दक्षिण कोरिया",
        ["SA"] = "साउदी अरब",
        ["JP"] = "जापान",
    };
}
