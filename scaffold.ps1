# Budgeteer Project Scaffolding Script
# Run this script to create the complete project structure

Write-Host "Creating Budgeteer project structure..." -ForegroundColor Green

# Create solution
dotnet new sln -n Budgeteer

# Create Aspire AppHost
dotnet new aspire-apphost -n Budgeteer.AppHost -o Budgeteer.AppHost
dotnet sln add Budgeteer.AppHost

# Create Service Defaults
dotnet new aspire-servicedefaults -n Budgeteer.ServiceDefaults -o Budgeteer.ServiceDefaults
dotnet sln add Budgeteer.ServiceDefaults

# Create Shared library
dotnet new classlib -n Budgeteer.Shared -o Budgeteer.Shared
dotnet sln add Budgeteer.Shared
Remove-Item Budgeteer.Shared\Class1.cs

# Create Accounts domain library
dotnet new classlib -n Budgeteer.Accounts -o Budgeteer.Accounts
dotnet sln add Budgeteer.Accounts
Remove-Item Budgeteer.Accounts\Class1.cs
dotnet add Budgeteer.Accounts reference Budgeteer.Shared
dotnet add Budgeteer.Accounts package Marten --version 7.34.3
dotnet add Budgeteer.Accounts package Marten.AspNetCore --version 7.34.3
dotnet add Budgeteer.Accounts package CsvHelper --version 33.0.1

# Create Budget domain library
dotnet new classlib -n Budgeteer.Budget -o Budgeteer.Budget
dotnet sln add Budgeteer.Budget
Remove-Item Budgeteer.Budget\Class1.cs
dotnet add Budgeteer.Budget reference Budgeteer.Shared
dotnet add Budgeteer.Budget package Marten --version 7.34.3
dotnet add Budgeteer.Budget package Marten.AspNetCore --version 7.34.3

# Create Blazor Web App
dotnet new blazor -n Budgeteer.Web -o Budgeteer.Web --interactivity Server
dotnet sln add Budgeteer.Web
dotnet add Budgeteer.Web reference Budgeteer.ServiceDefaults
dotnet add Budgeteer.Web reference Budgeteer.Accounts
dotnet add Budgeteer.Web reference Budgeteer.Budget
dotnet add Budgeteer.Web reference Budgeteer.Shared
dotnet add Budgeteer.Web package Marten --version 7.34.3
dotnet add Budgeteer.Web package Marten.AspNetCore --version 7.34.3
dotnet add Budgeteer.Web package Npgsql --version 8.0.5

# Add AppHost references
dotnet add Budgeteer.AppHost reference Budgeteer.Web
dotnet add Budgeteer.AppHost reference Budgeteer.ServiceDefaults
dotnet add Budgeteer.AppHost package Aspire.Hosting.PostgreSQL --version 9.0.0

Write-Host "`nProject structure created!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Run '.\create-source-files.ps1' to create all source code files"
Write-Host "2. Run 'dotnet restore' to restore packages"
Write-Host "3. Run 'dotnet build' to build the solution"
Write-Host "4. cd Budgeteer.AppHost && dotnet run' to start the application"
