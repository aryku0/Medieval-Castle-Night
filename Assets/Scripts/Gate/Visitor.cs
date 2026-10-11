/// <summary>What kind of person is at the gate.</summary>
public enum VisitorKind { Traveller, Merchant, Smuggler, Messenger, Lady }

/// <summary>The three calls the guard can make.</summary>
public enum GateVerdict { Admit, Deny, Arrest }

/// <summary>
/// One visitor and the papers they carry. Filled in by VisitorGenerator,
/// judged by GateRules. Plain data, no Unity objects.
/// </summary>
public class Visitor
{
    public VisitorKind kind;
    public string spokenName;   // the name they tell you
    public string description;  // placeholder for a portrait
    public string greeting;     // what they say when you open the hatch

    // Travel pass (everyone except ladies)
    public bool hasPass;
    public string passName;
    public string passTown;
    public int passExpiryDay;   // day of November 1605
    public string passStamp;

    // Lord's seal (messengers, night 2+)
    public bool hasLordsSeal;
    public string lordsSeal;

    // Health certificate (everyone, night 3)
    public bool hasHealthCert;
    public string healthCertName;
    public string healthCertSignature;

    // An ordinary traveller or merchant who bought a forged pass out of desperation (arrest them; they talk in the cells).
    public bool boughtForgery;

    // Ladies only: is she keeping you busy while an accomplice strikes?
    public bool isDecoy;

    public bool IsLady => kind == VisitorKind.Lady;
}
