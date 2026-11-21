// Use the same pinned Playwright package as browser tests; no PowerShell needed.
return Microsoft.Playwright.Program.Main(args.Length == 0 ? ["install", "chromium"] : args);
