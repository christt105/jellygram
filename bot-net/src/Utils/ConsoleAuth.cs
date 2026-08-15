using System.Text;
using TL;
using Bot.Services;

namespace Bot.Utils;

/// <summary>
/// One-shot interactive login for the Telegram user account, run as `dotnet Bot.dll auth`.
/// The verification code is read from the terminal on purpose: Telegram invalidates any login
/// code its servers see the account send through a chat, so it can never be typed into the bot.
/// </summary>
public static class ConsoleAuth
{
    public static async Task<int> RunAsync()
    {
        var apiIdRaw = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
        var apiHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");

        if (!int.TryParse(apiIdRaw, out var apiId) || string.IsNullOrWhiteSpace(apiHash))
        {
            Console.Error.WriteLine("TELEGRAM_API_ID and TELEGRAM_API_HASH must be set.");
            return 1;
        }

        var phone = Prompt("Phone number (e.g. +34612345678)");
        if (phone.Length == 0)
        {
            Console.Error.WriteLine("No phone number given.");
            return 1;
        }

        var sessionPath = UserClientService.SessionPath;
        var stagingPath = sessionPath + ".new";
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);

        bool isPremium;
        long userId;
        string name;

        try
        {
            await using (var sessionStream = File.Open(stagingPath, FileMode.Create, FileAccess.ReadWrite))
            using (var client = new WTelegram.Client(what => what switch
                   {
                       "api_id" => apiId.ToString(),
                       "api_hash" => apiHash,
                       "phone_number" => phone,
                       "verification_code" => PromptCode(),
                       "password" => PromptSecret("Two-factor cloud password"),
                       _ => null
                   }, sessionStream))
            {
                var user = await client.LoginUserIfNeeded();
                isPremium = (user.flags & User.Flags.premium) != 0;
                userId = user.id;
                name = user.first_name ?? "";
            }
        }
        catch (Exception ex)
        {
            File.Delete(stagingPath);
            Console.Error.WriteLine($"Login failed: {ex.Message}");
            return 1;
        }

        File.Move(stagingPath, sessionPath, overwrite: true);

        Console.WriteLine();
        Console.WriteLine($"Logged in as {name} ({userId}). Premium: {(isPremium ? "yes" : "no")}.");
        Console.WriteLine($"Session saved to {sessionPath}. Restart the bot to pick it up.");
        return 0;
    }

    private static string Prompt(string label)
    {
        Console.Write($"{label}: ");
        return (Console.ReadLine() ?? "").Trim();
    }

    private static string PromptCode()
    {
        var raw = Prompt("Verification code");
        return new string(raw.Where(char.IsDigit).ToArray());
    }

    private static string PromptSecret(string label)
    {
        if (Console.IsInputRedirected)
            return Prompt(label);

        Console.Write($"{label}: ");
        var buffer = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar))
                buffer.Append(key.KeyChar);
        }
    }
}
