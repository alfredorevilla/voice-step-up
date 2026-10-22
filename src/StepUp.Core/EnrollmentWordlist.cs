namespace StepUp.Core;

/// <summary>
/// Words for enrollment phrases. It shares no word with the challenge <c>Wordlist</c>, so recorded enrollment audio
/// gives an attacker none of the words a later challenge will ask for (TM-001 splicing).
/// </summary>
public static class EnrollmentWordlist
{
    public static readonly string[] Words =
    [
        "adventure", "alphabet", "balcony", "blossom", "bookshelf", "calendar", "carnival", "chestnut",
        "compass", "cucumber", "dashboard", "doorway", "elephant", "envelope", "evening", "festival",
        "fireplace", "flannel", "giraffe", "grapefruit", "handbag", "highway", "horizon", "hurricane",
        "inkwell", "jasmine", "kangaroo", "keyboard", "landscape", "lighthouse", "luggage", "machine",
        "mailbox", "mattress", "midnight", "mushroom", "notebook", "novel", "orchestra", "outdoor",
        "painter", "peanut", "pineapple", "playground", "porch", "postcard", "quilt", "rainbow",
        "raincoat", "sandwich", "scooter", "seashore", "shoulder", "skyline", "snowman", "sofa",
        "spaghetti", "staircase", "strawberry", "sunflower", "suitcase", "teacher", "thimble", "toothbrush",
        "traveler", "treasure", "trolley", "tuxedo", "vacation", "vegetable", "village", "volcano",
        "waffle", "watermelon", "weekend", "wardrobe", "wheelbarrow", "windmill", "workshop", "yogurt",
    ];
}
