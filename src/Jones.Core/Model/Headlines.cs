namespace Jones.Core.Model;

/// <summary>
/// The newspaper's 64 headlines, verbatim from `newspaper.sc` (script 215).
///
/// The index is the value the economy publishes in `global415`, which is why the table
/// is worth reading as documentation of the economy itself:
///   1-3   market crash, by severity â€” 1 is the worst, and its headline says so
///   5/6   gold good / bad          7/8   silver        9/10  pork bellies
///   11/12 blue chip                13/14 penny stocks
///   15    apartment robbery        16    street mugging
///   18/19 investments              20/21 the economy at large
///
/// Each index publishes the good news, and index + 1 the bad â€” exactly the
/// `headline` and `headline + 1` selection in `economicIndex.sc`.
/// </summary>
public static class Headlines
{
    public static readonly string[] All =
    [
        /*  0 */ "DUMMY",
        /*  1 */ "BANKS FALTER!\nSAVINGS LOST!  JOBS LOST!",
        /*  2 */ "SCANDAL ON WALL ST.  ECONOMY\nDROPS! UNEMPLOYMENT RISES",
        /*  3 */ "MORE S & L'S FAIL!\nECONOMY SUFFERS",
        /*  4 */ "INFLATION IS UP\nPRICES COULD SOAR!",
        /*  5 */ "GOLD OUTLOOK IS GOOD",
        /*  6 */ "GOLD INVESTMENTS\nLOOK TARNISHED",
        /*  7 */ "GOOD TIMES AHEAD FOR SILVER",
        /*  8 */ "EXPERTS AGREE\nGET OUT OF SILVER",
        /*  9 */ "PRESIDENT EATS PORK RINDS",
        /* 10 */ "GENERAL POPULATION HATES\nPORK RINDS",
        /* 11 */ "BLUE CHIP STOCKS\nOUTLOOK ROSEY",
        /* 12 */ "HARD TIMES AHEAD\nFOR BIG BLUE",
        /* 13 */ "PENNY STOCKS ARE WISE\nINVESTMENT",
        /* 14 */ "PENNY STOCKS NOT\nWORTH A DIME",
        /* 15 */ "WILD WILLY RIPS OFF\nANOTHER APARTMENT",
        /* 16 */ "WILD WILLY HAS LIFTED\nANOTHER WALLET",
        /* 17 */ "BIG LOTTERY WINNER!",
        /* 18 */ "INVESTMENTS RECOMMENDED",
        /* 19 */ "INVESTMENTS NOT RECOMMENDED",
        /* 20 */ "ECONOMIC OUTLOOK GOOD",
        /* 21 */ "ECONOMIC OUTLOOK POOR",
        /* 22 */ "UNEMPLOYMENT IS ON\nTHE RISE",
        /* 23 */ "UNEMPLOYMENT IS DOWN",
        /* 24 */ "NOBODY YOU KNOW\nWON THE LOTTO!",
        /* 25 */ "PRESIDENT HATES BROCCOLI",
        /* 26 */ "MORE FAST FOOD PLACES\nUSING SOYBEANS",
        /* 27 */ "SCHOOL ENROLLMENT UP",
        /* 28 */ "SCHOOL ENROLLMENT DOWN",
        /* 29 */ "THERE IS MONEY IN\nCOMPUTERS",
        /* 30 */ "HOUSING MARKET LOOKS GOOD",
        /* 31 */ "SALES OF NEWSPAPERS\nHAVE SKYROCKETED",
        /* 32 */ "PAWN SHOPS SERVE\nUSEFUL PURPOSE",
        /* 33 */ "NORTH SHORE OF BASS\nLAKE SINKS",
        /* 34 */ "ALICE COOPER GIVES BIRTH\nTO TWIN BOYS",
        /* 35 */ "COARSEGOLD PURCHASED BY\nJAPAN",
        /* 36 */ "SPACE QUEST III WINS\nBIG AWARD",
        /* 37 */ "ELVIS SIGHTED AT KFC\nIN OAKHURST",
        /* 38 */ "TALKING BEAR KIDNAPPED!\nFBI INVESTIGATING",
        /* 39 */ "KINGS QUEST XXIX GOES\nTO PRODUCTION",
        /* 40 */ "MR. WHIPPLE FOUND SQUEEZED\nTO DEATH IN APARTMENT",
        /* 41 */ "REAGAN'S NAP INTERRUPTS\nSPEECH",
        /* 42 */ "MOTHER GOOSE GETS DIVORCE!\nFEATHERS RUFFLED",
        /* 43 */ "MOTHER GOOSE SUSPECTED OF\nFOWL PLAY",
        /* 44 */ "SALMON BITING OFF NORTH\nSHORE OF BASS LAKE",
        /* 45 */ "NIXON MAKES ROCK VIDEO",
        /* 46 */ "FORD STUMBLES ON CURE",
        /* 47 */ "NEW GOVT STUDY SHOWS GAME\nPLAYERS GET SICK TOO!",
        /* 48 */ "IMELDA M. LOOKING FOR\nA FEW GOOD SHOES",
        /* 49 */ "NANCY IS LOOKING FOR A\nNEW DRESS",
        /* 50 */ "TYpEsETTrs U ion\nshr ds Agre mNt!",
        /* 51 */ "FIREMEN ARE ALWAYS\nIN HEAT",
        /* 52 */ "PRESIDENT FINALLY EATS\nBROCCOLI",
        /* 53 */ "PRESIDENT EATS BROCCOLI\nAND LIVES!",
        /* 54 */ "STUDY SHOWS WE HAVE\nMORE LEISURE TIME",
        /* 55 */ "EXTRA! EXTRA!\n",
        /* 56 */ "TORNADO KILLS 8 THEN\nCOMMITS SUICIDE!",
        /* 57 */ "CIGARETTES FOUND TO CAUSE\nLABORATORY ANIMALS!",
        /* 58 */ "COURTS JAMMED!\nWAPNER REINSTATED!",
        /* 59 */ "GRAND CANYON DESIGNATED\nNATIONAL LANDFILL!",
        /* 60 */ "CELEBRITY BULLFIGHTING\nDISASTER; LESLEY GORED",
        /* 61 */ "GURUKA SINGH GETS HAIRCUT!\nPHOTOS UNDER WRAPS!",
        /* 62 */ "RAP GROUP ARRESTED FOR\nNOT STARTING RIOT!",
        /* 63 */ "TRAILER PARK DEMOLISHES\n6 TORNADOES!",
    ];

    /// <summary>The first filler headline, used when there is no real news.</summary>
    public const int FirstFiller = 24;

    public static string Get(int id) =>
        id > 0 && id < All.Length ? All[id] : All[FirstFiller];
}
