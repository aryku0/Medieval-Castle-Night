using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes up visitors and their papers. Honest people sometimes have a problem
/// with their papers (deny); smugglers always carry a forgery (arrest), and so do the odd
/// desperate traveller or merchant who bought one (AddHonestForgers).
/// </summary>
public static class VisitorGenerator
{
    static readonly string[] FirstNames = { "Thomas", "John", "William", "Richard", "Edmund", "Walter", "Henry", "Robert", "Hugh", "Giles", "Martin", "Simon" };
    static readonly string[] Surnames = { "Hale", "Cooper", "Fletcher", "Wright", "Mason", "Barker", "Thatcher", "Ashdown", "Crowe", "Pike", "Tanner", "Webb" };

    static readonly string[] TravellerLooks =
    {
        "A tired traveller in a mud-spattered cloak.", "A young man with a pack on his back and wet boots.",
        "A greying pilgrim leaning on a staff.", "A thin man with a lantern and a nervous smile.",
    };
    static readonly string[] MerchantLooks =
    {
        "A stout merchant with a grey beard and rings on his fingers.", "A cloth trader with a cart of wool behind him.",
        "A spice seller wrapped in a fur-lined coat.", "A wine merchant, red-nosed and cheerful.",
    };
    static readonly string[] SmugglerLooks =
    {
        "A broad man with a hood pulled low.", "A wiry man who keeps glancing over his shoulder.",
        "A man with a heavy sack he won't put down.", "A scarred sailor smelling of tar and gunpowder.",
    };
    static readonly string[] MessengerLooks =
    {
        "A breathless rider with a satchel of letters.", "A boy in livery, soaked from the rain.",
        "A grim courier with a sealed leather case.",
    };
    static readonly string[] LadyLooks =
    {
        "A lady in a green gown, twirling a ribbon.", "A lady with a painted fan and a sly smile.",
        "A lady with rosy cheeks and a basket of apples.",
    };

    static readonly string[] TravellerLines = { "Evening, guard. Just need a roof for the night.", "Please, it's freezing out here.", "I've walked since dawn. May I pass?" };
    static readonly string[] MerchantLines = { "Goods for the castle kitchens, good sir.", "I've business with the steward, let me through.", "Fine wares for fine lords!" };
    static readonly string[] SmugglerLines = { "Nothing to see here, friend. Just let me in.", "Quick now, I haven't got all night.", "All my papers are proper, I swear it." };
    static readonly string[] MessengerLines = { "Urgent letters for the castle!", "Post from the south. Let me in, quickly.", "I carry word for his lordship." };
    static readonly string[] ForgerLines = { "Please, my children are inside the walls.", "I paid good money for this pass. It's proper, isn't it?", "I beg you, sir, just let me through." };
    static readonly string[] LadyLines = { "Oh, what a handsome guard...", "Such a lonely night to stand watch alone.", "Won't you keep a lady company?" };

    static T Pick<T>(IList<T> list) => list[Random.Range(0, list.Count)];

    static string RandomName() => Pick(FirstNames) + " " + Pick(Surnames);

    static string DifferentFirstName(string fullName)
    {
        string surname = fullName.Substring(fullName.IndexOf(' ') + 1);
        string first;
        do { first = Pick(FirstNames); } while (fullName.StartsWith(first + " "));
        return first + " " + surname;
    }

    /// <summary>The kinds of visitor for one night, shuffled.</summary>
    public static List<VisitorKind> Lineup(int night, int count)
    {
        var kinds = new List<VisitorKind> { VisitorKind.Lady, VisitorKind.Smuggler };
        if (night >= 2) kinds.Add(VisitorKind.Messenger);
        if (night >= 2 && count > 5) kinds.Add(VisitorKind.Messenger);
        while (kinds.Count < count) kinds.Add(Random.value < 0.5f ? VisitorKind.Traveller : VisitorKind.Merchant);
        if (kinds.Count > count) kinds.RemoveRange(count, kinds.Count - count);

        for (int i = kinds.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
        }
        return kinds;
    }

    public static Visitor Create(VisitorKind kind, int night, float ladyDecoyChance)
    {
        var v = new Visitor { kind = kind };
        int today = GateRules.Day(night);

        switch (kind)
        {
            case VisitorKind.Lady:
                v.spokenName = "A lady";
                v.description = Pick(LadyLooks);
                v.greeting = Pick(LadyLines);
                v.isDecoy = Random.value < ladyDecoyChance;
                return v; // no papers

            case VisitorKind.Traveller: v.description = Pick(TravellerLooks); v.greeting = Pick(TravellerLines); break;
            case VisitorKind.Merchant: v.description = Pick(MerchantLooks); v.greeting = Pick(MerchantLines); break;
            case VisitorKind.Smuggler: v.description = Pick(SmugglerLooks); v.greeting = Pick(SmugglerLines); break;
            case VisitorKind.Messenger: v.description = Pick(MessengerLooks); v.greeting = Pick(MessengerLines); break;
        }

        // Start with a perfectly valid set of papers...
        v.spokenName = RandomName();
        GiveValidPapers(v, night);

        // ...then break something.
        if (kind == VisitorKind.Smuggler) AddForgery(v, night);
        else
        {
            // Smallpox night: most people can't get in.
            float flawChance = GateRules.HealthCertRequired(night) ? 0.65f : 0.45f;
            if (Random.value < flawChance) AddHonestProblem(v, night, today);
        }
        return v;
    }

    /// <summary>
    /// Turn 1-2 of tonight's travellers and merchants into honest forgers: ordinary people who
    /// bought a forged pass. Arresting them is correct, and unlike smugglers they'll talk in the cells.
    /// </summary>
    public static void AddHonestForgers(List<Visitor> planned, int night)
    {
        var candidates = planned.FindAll(v => v.kind == VisitorKind.Traveller || v.kind == VisitorKind.Merchant);
        int count = Mathf.Min(candidates.Count, Random.Range(1, 3));
        for (int i = 0; i < count; i++)
        {
            int j = Random.Range(i, candidates.Count);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            MakeHonestForger(candidates[i], night);
        }
    }

    public static void MakeHonestForger(Visitor v, int night)
    {
        GiveValidPapers(v, night); // clear any honest problem first, so the forgery is the only fault
        AddForgery(v, night);
        v.boughtForgery = true;
        v.greeting = Pick(ForgerLines);
    }

    static void GiveValidPapers(Visitor v, int night)
    {
        int today = GateRules.Day(night);
        v.hasPass = true;
        v.passName = v.spokenName;
        v.passTown = Pick(GateRules.ApprovedTowns);
        v.passExpiryDay = today + Random.Range(0, 6);
        v.passStamp = GateRules.GenuineStamp;
        if (v.kind == VisitorKind.Messenger && GateRules.MessengersNeedSeal(night))
        {
            v.hasLordsSeal = true;
            v.lordsSeal = GateRules.GenuineLordsSeal;
        }
        if (GateRules.HealthCertRequired(night))
        {
            v.hasHealthCert = true;
            v.healthCertName = v.spokenName;
            v.healthCertSignature = GateRules.GenuineHealthSignature;
        }
        else
        {
            v.hasHealthCert = false;
            v.healthCertName = null;
            v.healthCertSignature = null;
        }
    }

    static void AddForgery(Visitor v, int night)
    {
        var options = new List<int> { 0, 1 };
        if (GateRules.HealthCertRequired(night)) options.Add(2);
        switch (Pick(options))
        {
            case 0: v.passStamp = Pick(GateRules.ForgedStamps); break;
            case 1: v.passTown = GateRules.ForgedTownSpellings[v.passTown]; break;
            case 2: v.healthCertSignature = Pick(GateRules.ForgedHealthSignatures); break;
        }
    }

    static void AddHonestProblem(Visitor v, int night, int today)
    {
        var options = new List<int> { 0, 1, 2 };
        if (v.kind == VisitorKind.Messenger && GateRules.MessengersNeedSeal(night)) options.Add(3);
        if (GateRules.HealthCertRequired(night)) { options.Add(4); options.Add(4); options.Add(5); }

        switch (Pick(options))
        {
            case 0: v.passExpiryDay = today - Random.Range(1, 11); break;          // expired
            case 1: v.passTown = Pick(GateRules.UnapprovedTowns); break;            // wrong town
            case 2: v.passName = DifferentFirstName(v.spokenName); break;           // name mismatch
            case 3: v.hasLordsSeal = false; v.lordsSeal = null; break;              // messenger without seal
            case 4: v.hasHealthCert = false; break;                                 // no certificate
            case 5: v.healthCertName = DifferentFirstName(v.spokenName); break;     // someone else's certificate
        }
    }
}
