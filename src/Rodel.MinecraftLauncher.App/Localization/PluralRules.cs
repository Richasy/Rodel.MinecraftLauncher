// Copyright (c) Richasy. All rights reserved.

using Windows.Win32;
using Windows.Win32.Globalization;

namespace Rodel.MinecraftLauncher.App.Localization;

/// <summary>Selects cardinal categories through Windows ICU without retaining native handles.</summary>
internal static class PluralRules
{
    internal static unsafe string? SelectCardinal(int count, string language)
    {
        try
        {
            var status = UErrorCode.U_ZERO_ERROR;
            // The ICU locale is a null-terminated UTF-8 string, including when using the generated string overload.
            var rules = PInvoke.uplrules_open(language + '\0', ref status);
            if (rules is null)
                return null;
            try
            {
                if ((int)status > 0)
                    return null;
                const int capacity = 8;
                var keyword = stackalloc ushort[capacity];
                status = UErrorCode.U_ZERO_ERROR;
                var length = PInvoke.uplrules_select(rules, count, keyword, capacity, &status);
                // Negative ICU statuses are success warnings. Keywords are UTF-16, not UTF-8.
                return (int)status <= 0 && length is > 0 and < capacity
                    ? new string((char*)keyword, 0, length)
                    : null;
            }
            finally
            {
                PInvoke.uplrules_close(rules);
            }
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}