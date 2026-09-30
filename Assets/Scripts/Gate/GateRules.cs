using System.Collections.Generic;
using System.Text;

/// <summary>
/// Tonight's rules at the gate: what papers are needed, what counts as forged,
/// the Captain's orders and the rulebook text. Everything the player is judged on lives here.
/// </summary>
public static class GateRules
{
    // Night 1 is the 3rd of November 1605, so night 3 falls on the 5th...
    public const int FirstNightDay = 3;

    public static readonly string[] ApprovedTowns = { "York", "Lincoln", "Durham", "Chester" };
    public static readonly string[] UnapprovedTowns = { "Carlisle", "Norwich", "Dover", "Bristol" };

    /// <summary>Forgers misspell the town. Proof of forgery.</summary>
    public static readonly Dictionary<string, string> ForgedTownSpellings = new Dictionary<string, string>
    {
        { "York", "Yorke" }, { "Lincoln", "Lincon" }, { "Durham", "Durhem" }, { "Chester", "Chestre" },
    };

    public const string GenuineStamp = "red ink, crown upright";
    public static readonly string[] ForgedStamps =
    {
        "blue ink, crown upright", "red ink, crown upside down", "smudged brown ink, no crown",
    };

    public const string GenuineLordsSeal = "black wax, stag's head";
    public static readonly string[] ForgedLordsSeals = { "black wax, horse's head", "green wax, stag's head" };

    public const string GenuineHealthSignature = "Dr. Whitlock, physician";
    public static readonly string[] ForgedHealthSignatures = { "Dr. Whitlocke, physician", "Dr. Whitlock (no title, shaky hand)" };

    public static int Day(int night) => FirstNightDay + night - 1;

    public static string DateText(int day) => $"{day}{Suffix(day)} November 1605";

    static string Suffix(int day)
    {
        if (day % 100 >= 11 && day % 100 <= 13) return "th";
        switch (day % 10) { case 1: return "st"; case 2: return "nd"; case 3: return "rd"; default: return "th"; }
    }

    public static bool MessengersNeedSeal(int night) => night >= 2;
    public static bool HealthCertRequired(int night) => night >= 3;

    static bool IsApproved(string town)
    {
        foreach (var t in ApprovedTowns) if (t == town) return true;
        return false;
    }

    static bool IsForgedSpelling(string town)
    {
        foreach (var pair in ForgedTownSpellings) if (pair.Value == town) return true;
        return false;
    }

    /// <summary>The correct call for this visitor tonight, and why.</summary>
    public static GateVerdict Evaluate(Visitor v, int night, out string reason)
    {
        int today = Day(night);

        // 1) Forgery is proof: arrest.
        if (v.hasPass && v.passStamp != GenuineStamp) { reason = "forged stamp on the travel pass"; return GateVerdict.Arrest; }
        if (v.hasPass && IsForgedSpelling(v.passTown)) { reason = "forged travel pass (town misspelled)"; return GateVerdict.Arrest; }
        if (MessengersNeedSeal(night) && v.kind == VisitorKind.Messenger && v.hasLordsSeal && v.lordsSeal != GenuineLordsSeal)
        { reason = "forged lord's seal"; return GateVerdict.Arrest; }
        if (HealthCertRequired(night) && v.hasHealthCert && v.healthCertSignature != GenuineHealthSignature)
        { reason = "forged health certificate"; return GateVerdict.Arrest; }

        // 2) Wrong or missing papers: turn away.
        if (!v.hasPass) { reason = "no travel pass"; return GateVerdict.Deny; }
        if (v.passExpiryDay < today) { reason = "travel pass expired"; return GateVerdict.Deny; }
        if (!IsApproved(v.passTown)) { reason = $"{v.passTown} is not an approved town"; return GateVerdict.Deny; }
        if (v.passName != v.spokenName) { reason = "name on the pass doesn't match"; return GateVerdict.Deny; }
        if (MessengersNeedSeal(night) && v.kind == VisitorKind.Messenger && !v.hasLordsSeal)
        { reason = "messenger without the lord's seal"; return GateVerdict.Deny; }
        if (HealthCertRequired(night) && !v.hasHealthCert) { reason = "no health certificate"; return GateVerdict.Deny; }
        if (HealthCertRequired(night) && v.healthCertName != v.spokenName) { reason = "health certificate is in another name"; return GateVerdict.Deny; }

        reason = "papers in order";
        return GateVerdict.Admit;
    }

    public static string Orders(int night)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>Captain's orders: night of the {DateText(Day(night))}</b>\n");
        if (night == 1)
            sb.AppendLine("Travellers will come to the gate tonight. Check every one of them.\n");
        sb.AppendLine("• Admit only people from an <b>approved town</b>: " + string.Join(", ", ApprovedTowns) + ".");
        sb.AppendLine("• Their <b>travel pass</b> must still be valid tonight, and the name on it must match the name they give.");
        sb.AppendLine($"• A genuine pass stamp is <b>{GenuineStamp}</b>.");
        if (MessengersNeedSeal(night))
            sb.AppendLine($"• <b>New:</b> messengers must carry <b>Lord Ashby's seal ({GenuineLordsSeal})</b>. Take their letters.");
        if (HealthCertRequired(night))
            sb.AppendLine($"• <b>SMALLPOX in the lower town.</b> Nobody enters without a <b>health certificate</b> in their own name, signed by <b>{GenuineHealthSignature}</b>.");
        sb.AppendLine("\nTurn away anyone whose papers are wrong. <b>Arrest</b> anyone carrying forged papers.");
        sb.AppendLine("The rulebook is on your desk at the gate.");
        return sb.ToString();
    }

    public static string Rulebook(int night)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>RULEBOOK</b>   Tonight: {DateText(Day(night))}\n");
        sb.AppendLine("<b>Approved towns:</b> " + string.Join(", ", ApprovedTowns));
        sb.AppendLine($"<b>Genuine pass stamp:</b> {GenuineStamp}");
        if (MessengersNeedSeal(night)) sb.AppendLine($"<b>Lord Ashby's seal:</b> {GenuineLordsSeal}");
        if (HealthCertRequired(night)) sb.AppendLine($"<b>Health certificates:</b> signed by {GenuineHealthSignature}");
        sb.AppendLine("\n<b>DENY</b> if: no pass · pass expired · town not approved · name doesn't match"
            + (MessengersNeedSeal(night) ? " · messenger has no seal" : "")
            + (HealthCertRequired(night) ? " · no health certificate, or it's in another name" : ""));
        sb.AppendLine("<b>ARREST</b> if anything is forged: wrong stamp, misspelled town"
            + (MessengersNeedSeal(night) ? ", fake seal" : "")
            + (HealthCertRequired(night) ? ", fake signature" : "") + ".");
        sb.AppendLine("<b>ADMIT</b> only if everything is in order.");
        return sb.ToString();
    }
}
