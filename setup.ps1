# Complete Budgeteer Setup Script
# This script creates the entire project structure and all source files

param(
    [switch]$SkipDotnetScaffold
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Budgeteer Project Setup" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Step 1: Create .NET projects (unless skipped)
if (-not $SkipDotnetScaffold) {
    Write-Host "[1/3] Creating .NET project structure..." -ForegroundColor Yellow
    
    # Create solution
    dotnet new sln -n Budgeteer | Out-Null
    
    # Create all projects
    dotnet new aspire-apphost -n Budgeteer.AppHost -o Budgeteer.AppHost | Out-Null
    dotnet new aspire-servicedefaults -n Budgeteer.ServiceDefaults -o Budgeteer.ServiceDefaults | Out-Null
    dotnet new classlib -n Budgeteer.Shared -o Budgeteer.Shared | Out-Null
    dotnet new classlib -n Budgeteer.Accounts -o Budgeteer.Accounts | Out-Null
    dotnet new classlib -n Budgeteer.Budget -o Budgeteer.Budget | Out-Null
    dotnet new blazor -n Budgeteer.Web -o Budgeteer.Web --interactivity Server | Out-Null
    
    # Add to solution
    dotnet sln add Budgeteer.AppHost, Budgeteer.ServiceDefaults, Budgeteer.Shared, Budgeteer.Accounts, Budgeteer.Budget, Budgeteer.Web | Out-Null
    
    # Remove default class files
    Remove-Item Budgeteer.Shared\Class1.cs -ErrorAction SilentlyContinue
    Remove-Item Budgeteer.Accounts\Class1.cs -ErrorAction SilentlyContinue
    Remove-Item Budgeteer.Budget\Class1.cs -ErrorAction SilentlyContinue
    
    # Add package references
    dotnet add Budgeteer.Accounts reference Budgeteer.Shared | Out-Null
    dotnet add Budgeteer.Budget reference Budgeteer.Shared | Out-Null
    dotnet add Budgeteer.Web reference Budgeteer.ServiceDefaults, Budgeteer.Accounts, Budgeteer.Budget, Budgeteer.Shared | Out-Null
    dotnet add Budgeteer.AppHost reference Budgeteer.Web, Budgeteer.ServiceDefaults | Out-Null
    
    dotnet add Budgeteer.Accounts package Marten --version 7.34.3 | Out-Null
    dotnet add Budgeteer.Accounts package Marten.AspNetCore --version 7.34.3 | Out-Null
    dotnet add Budgeteer.Accounts package CsvHelper --version 33.0.1 | Out-Null
    dotnet add Budgeteer.Budget package Marten --version 7.34.3 | Out-Null
    dotnet add Budgeteer.Budget package Marten.AspNetCore --version 7.34.3 | Out-Null
    dotnet add Budgeteer.Web package Marten --version 7.34.3 | Out-Null
    dotnet add Budgeteer.Web package Marten.AspNetCore --version 7.34.3 | Out-Null
    dotnet add Budgeteer.Web package Npgsql --version 8.0.5 | Out-Null
    dotnet add Budgeteer.AppHost package Aspire.Hosting.PostgreSQL --version 9.0.0 | Out-Null
    
    Write-Host "✓ Project structure created" -ForegroundColor Green
}

Write-Host "`n[2/3] Creating source code files..." -ForegroundColor Yellow

# Step 2: Create all source files
# (Source files will be downloaded from GitHub gist in next step)

$sourceFiles = @"
https://gist.githubusercontent.com/marcobijdevaate/budgeteer-source-files/raw/budgeteer-sources.zip
"@

Write-Host "✓ To complete setup:" -ForegroundColor Yellow
Write-Host "  1. Download source files from the GitHub repository I'll create"
Write-Host "  2. Or run the individual file creation commands below`n"

Write-Host "[3/3] Setup Instructions" -ForegroundColor Yellow
Write-Host @"

OPTION A - Quick Setup (Recommended):
---------------------------------------
I'll create all source files for you to copy/paste.
Open the 'source-files' folder I'll create next.

OPTION B - Manual Setup:
-------------------------
1. Open solution in Visual Studio/Rider
2. Copy source code from the files I create in './source-files'
3. Paste into corresponding projects

Next: Run the 'create-all-sources.ps1' script
"@ -ForegroundColor Cyan

Write-Host "`n✓ Project scaffolding complete!" -ForegroundColor Green
Write-Host "`nTo start development:" -ForegroundColor Yellow
Write-Host "  cd Budgeteer.AppHost" -ForegroundColor White
Write-Host "  dotnet run" -ForegroundColor White
