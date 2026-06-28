namespace Budgeteer.Budget.Categorization;

/// <summary>
/// Default (seed) categorization rules, tuned for common Dutch merchants and payees.
/// Keywords are matched case-insensitively against the payee + description.
/// </summary>
public static class DefaultRules
{
    /// <summary>(keyword, category) pairs seeded on first run.</summary>
    public static readonly (string Keyword, string Category)[] Entries =
    {
        // Groceries
        ("albert heijn", "Groceries"), ("ah filiaal", "Groceries"), ("ah to go", "Groceries"),
        ("jumbo", "Groceries"), ("lidl", "Groceries"), ("aldi", "Groceries"),
        ("plus ", "Groceries"), ("dirk", "Groceries"), ("spar", "Groceries"), ("picnic", "Groceries"),

        // Dining / takeaway
        ("thuisbezorgd", "Dining"), ("uber eats", "Dining"), ("ubereats", "Dining"),
        ("dominos", "Dining"), ("mcdonald", "Dining"), ("restaurant", "Dining"),
        ("cafe", "Dining"), ("starbucks", "Dining"),

        // Subscriptions / digital
        ("spotify", "Subscriptions"), ("netflix", "Subscriptions"), ("disney", "Subscriptions"),
        ("videoland", "Subscriptions"), ("hbo", "Subscriptions"), ("youtube", "Subscriptions"),
        ("apple.com/bill", "Subscriptions"), ("google", "Subscriptions"),
        ("microsoft", "Subscriptions"), ("openai", "Subscriptions"), ("amazon prime", "Subscriptions"),

        // Shopping
        ("bol.com", "Shopping"), ("coolblue", "Shopping"), ("amazon", "Shopping"),
        ("zalando", "Shopping"), ("action", "Shopping"), ("hema", "Shopping"),
        ("ikea", "Shopping"), ("mediamarkt", "Shopping"),

        // Housing / rent
        ("huur", "Housing"), ("verhuurder", "Housing"), ("vve ", "Housing"), ("hypotheek", "Housing"),

        // Utilities
        ("energie", "Utilities"), ("eneco", "Utilities"), ("vattenfall", "Utilities"),
        ("essent", "Utilities"), ("greenchoice", "Utilities"), ("vitens", "Utilities"),
        ("pwn", "Utilities"), ("ziggo", "Utilities"), ("kpn", "Utilities"),
        ("vodafone", "Utilities"), ("t-mobile", "Utilities"), ("odido", "Utilities"),

        // Transport / fuel
        ("ns groep", "Transport"), ("ns-", "Transport"), ("ov-chip", "Transport"),
        ("ovpay", "Transport"), ("gvb", "Transport"), ("ret ", "Transport"), ("htm", "Transport"),
        ("shell", "Fuel"), ("bp ", "Fuel"), ("esso", "Fuel"), ("tinq", "Fuel"), ("tango", "Fuel"),

        // Health / insurance
        ("zorgverzekering", "Health & Insurance"), ("vgz", "Health & Insurance"),
        ("zilveren kruis", "Health & Insurance"), ("menzis", "Health & Insurance"),
        ("apotheek", "Health & Insurance"), ("tandarts", "Health & Insurance"),

        // Sport
        ("basic fit", "Sport"), ("basic-fit", "Sport"), ("sportschool", "Sport"), ("fitness", "Sport"),

        // Taxes
        ("belastingdienst", "Taxes"), ("gemeente", "Taxes"),

        // Income
        ("salaris", "Salary"), ("salary", "Salary"), ("loon", "Salary"),
    };
}
