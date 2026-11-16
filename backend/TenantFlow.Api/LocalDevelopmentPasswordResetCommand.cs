using Microsoft.AspNetCore.Identity;
using TenantFlow.Api.Models;

namespace TenantFlow.Api;

internal static class LocalDevelopmentPasswordResetCommand
{
    private const string TargetAccountEmail = "testuser@tenantflow.com";
    private const int MaximumPasswordLength = 1024;

    public static async Task<int> RunAsync(IServiceProvider services, string[] arguments)
    {
        if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            Console.Error.WriteLine("The local password reset command is available only in Development.");
            return 1;
        }

        if (arguments.Length != 1
            || !string.Equals(arguments[0], TargetAccountEmail, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"Usage: --reset-local-password {TargetAccountEmail} (Development only).");
            return 1;
        }

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("Run this command from an interactive PowerShell console.");
            return 1;
        }

        try
        {
            return await ResetPasswordAsync(services);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Password reset cancelled. The security stamp may already have been initialized.");
            return 1;
        }
        catch (Exception)
        {
            // Exception messages can contain sensitive data from stores or token providers.
            Console.Error.WriteLine(
                "Password reset could not be completed. Check the local database and Identity configuration. "
                + "The security stamp may already have been initialized.");
            return 1;
        }
    }

    private static async Task<int> ResetPasswordAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(TargetAccountEmail);
        if (user is null)
        {
            Console.Error.WriteLine("No account was found for that email address.");
            return 1;
        }

        var originalUserId = user.Id;
        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            var stampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                Console.Error.WriteLine("Security stamp initialization failed. Password reset was not attempted.");
                WriteIdentityErrors(stampResult);
                return 1;
            }
        }

        // A fresh scope avoids returning the original EF-tracked user when reloading.
        await using var resetScope = services.CreateAsyncScope();
        userManager = resetScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        user = await userManager.FindByEmailAsync(TargetAccountEmail);
        if (user is null || user.Id != originalUserId || string.IsNullOrWhiteSpace(user.SecurityStamp))
        {
            Console.Error.WriteLine("The existing account could not be reloaded with an initialized security stamp.");
            return 1;
        }

        var password = ReadHiddenPassword("New password: ");
        var confirmation = ReadHiddenPassword("Confirm new password: ");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("The passwords did not match. The password was not changed; the security stamp may have been initialized.");
            return 1;
        }

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, resetToken, password);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine("Password reset failed. Use at least 12 characters, 4 distinct characters, uppercase, lowercase, a digit and a symbol.");
            WriteIdentityErrors(result);
            return 1;
        }

        Console.WriteLine("Password reset succeeded. Existing tenant memberships were not changed.");
        return 0;
    }

    private static void WriteIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            // Do not print descriptions: a custom password validator could include input.
            Console.Error.WriteLine($"Identity error: {error.Code}");
        }
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
                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    throw new OperationCanceledException();
                }

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
