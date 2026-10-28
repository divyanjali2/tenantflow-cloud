using Microsoft.AspNetCore.Identity;
using TenantFlow.Api.Models;

namespace TenantFlow.Api;

internal static class LocalDevelopmentPasswordResetCommand
{
    private const int MaximumPasswordLength = 1024;

    public static async Task<int> RunAsync(IServiceProvider services, string[] arguments)
    {
        if (arguments.Length != 1
            || string.IsNullOrWhiteSpace(arguments[0])
            || !string.Equals(arguments[0], arguments[0].Trim(), StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Usage: --reset-local-password <account-email> (Development only).");
            return 1;
        }

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("Run this command from an interactive PowerShell console.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(arguments[0]);
        if (user is null)
        {
            Console.Error.WriteLine("No account was found for that email address.");
            return 1;
        }

        var password = ReadHiddenPassword("New password: ");
        var confirmation = ReadHiddenPassword("Confirm new password: ");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The passwords did not match. No changes were made.");
            return 1;
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, resetToken, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine($"{error.Code}: {error.Description}");
            }

            return 1;
        }

        Console.WriteLine("Password reset succeeded. Existing tenant memberships were not changed.");
        return 0;
    }

    private static string ReadHiddenPassword(string prompt)
    {
        Console.Write(prompt);
        var buffer = new char[MaximumPasswordLength];
        var length = 0;

        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return new string(buffer, 0, length);
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (length > 0)
                    {
                        length--;
                    }

                    continue;
                }

                if (char.IsControl(key.KeyChar))
                {
                    continue;
                }

                if (length == buffer.Length)
                {
                    throw new InvalidOperationException(
                        $"Password input cannot exceed {MaximumPasswordLength} characters.");
                }

                buffer[length++] = key.KeyChar;
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }
}
