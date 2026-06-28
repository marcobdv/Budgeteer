# Budgeteer - Complete Project Creation Script
# Creates entire project structure with all source files
# Run this from an empty Budgeteer directory

Write-Host "Creating Budgeteer project..." -ForegroundColor Cyan

# 1. Create .NET Projects
Write-Host ""
Write-Host "[1/5] Creating .NET projects..." -ForegroundColor Yellow

dotnet new sln -n Budgeteer
dotnet new aspire-apphost -n Budgeteer.AppHost -o Budgeteer.AppHost
dotnet new aspire-servicedefaults -n Budgeteer.ServiceDefaults -o Budgeteer.ServiceDefaults
dotnet new classlib -n Budgeteer.Shared -o Budgeteer.Shared
dotnet new classlib -n Budgeteer.Accounts -o Budgeteer.Accounts
dotnet new classlib -n Budgeteer.Budget -o Budgeteer.Budget
dotnet new blazor -n Budgeteer.Web -o Budgeteer.Web --interactivity Server

# Add to solution
dotnet sln add **/*.csproj

# Clean up default files
Remove-Item Budgeteer.Shared\Class1.cs -ErrorAction SilentlyContinue
Remove-Item Budgeteer.Accounts\Class1.cs -ErrorAction SilentlyContinue
Remove-Item Budgeteer.Budget\Class1.cs -ErrorAction SilentlyContinue

Write-Host "[2/5] Adding package references..." -ForegroundColor Yellow

# Add references
dotnet add Budgeteer.Accounts reference Budgeteer.Shared
dotnet add Budgeteer.Budget reference Budgeteer.Shared
dotnet add Budgeteer.Web reference Budgeteer.ServiceDefaults
dotnet add Budgeteer.Web reference Budgeteer.Accounts
dotnet add Budgeteer.Web reference Budgeteer.Budget
dotnet add Budgeteer.Web reference Budgeteer.Shared
dotnet add Budgeteer.AppHost reference Budgeteer.Web
dotnet add Budgeteer.AppHost reference Budgeteer.ServiceDefaults

# Add NuGet packages
dotnet add Budgeteer.Accounts package Marten
dotnet add Budgeteer.Accounts package Marten.AspNetCore
dotnet add Budgeteer.Accounts package CsvHelper
dotnet add Budgeteer.Budget package Marten
dotnet add Budgeteer.Budget package Marten.AspNetCore
dotnet add Budgeteer.Web package Marten
dotnet add Budgeteer.Web package Marten.AspNetCore
dotnet add Budgeteer.Web package Npgsql
dotnet add Budgeteer.AppHost package Aspire.Hosting.PostgreSQL

Write-Host ""
Write-Host "SUCCESS: .NET projects created!" -ForegroundColor Green
Write-Host ""
Write-Host "Next: Let me know and I will create all source files" -ForegroundColor Yellow
