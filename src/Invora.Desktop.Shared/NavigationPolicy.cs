namespace Invora.Desktop;

internal static class NavigationPolicy
{
    internal static bool TryLocalOrigin(string text, out Uri? origin)
    {
        origin = null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            (uri.Host != "127.0.0.1" && uri.Host != "localhost") || uri.Port is < 1 or > 65535 ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        origin = uri; return true;
    }

    internal static bool IsInternal(Uri origin, string text)
    {
        if (text.StartsWith("blob:", StringComparison.Ordinal)) text = text[5..];
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == origin.Scheme &&
               uri.Host == origin.Host && uri.Port == origin.Port && uri.UserInfo.Length == 0;
    }

    internal static bool IsExternal(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.UserInfo.Length == 0 && uri.Scheme is "http" or "https" or "mailto" or "tel" or "sms";
}
