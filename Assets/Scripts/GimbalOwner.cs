public enum GimbalController
{
    None,
    Search,
    PAT
}

public static class GimbalOwner
{
    public static GimbalController Current { get; set; } = GimbalController.None;
}
