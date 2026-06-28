# Getting Started with Budgeteer

## ✅ Project Setup Complete!

All source code files have been created. You're ready to run the application!

## 🚀 Quick Start

### Step 1: Restore NuGet Packages

```powershell
dotnet restore
```

### Step 2: Build the Solution

```powershell
dotnet build
```

### Step 3: Run the Application

```powershell
cd Budgeteer.AppHost
dotnet run
```

**What happens:**
- Aspire pulls PostgreSQL Docker images (first time only - ~2 minutes)
- Starts 2 PostgreSQL containers:
  - `postgres-accounts` (Account domain event store)
  - `postgres-budget` (Budget domain event store)
- Starts Blazor web app on http://localhost:5000 (or similar)
- Opens Aspire Dashboard on http://localhost:15000

## 📱 Using the Application

### 1. Create Your First Account

- Navigate to **Accounts** page
- Click **Create Account**
- Fill in:
  - Name: "ING Checking"
  - Type: Checking
  - Initial Balance: 1000.00
- Click **Create**

**Behind the scenes:**
- `AccountCreated` event is stored in `accounts-eventstore` PostgreSQL database
- Account aggregate is projected from events

### 2. Add a Transaction

- Navigate to **Transactions** page
- Click **Add Transaction**
- Select your account
- Fill in:
  - Date: Today
  - Description: "Groceries at Albert Heijn"
  - Amount: -45.50 (negative = expense)
  - Payee: "Albert Heijn"
- Click **Add Transaction**

**Behind the scenes:**
1. `TransactionRecorded` event stored in Account domain
2. `TransactionEventHandler` receives event (in-process)
3. `ExpenseRecorded` event created in Budget domain
4. Both events in separate PostgreSQL databases

### 3. View Event Stores

**Option A: Aspire Dashboard**
- Go to http://localhost:15000
- Click on PostgreSQL containers
- View connection strings

**Option B: pgAdmin or SQL Client**
```sql
-- Connect to accounts database
SELECT * FROM accounts.mt_events ORDER BY timestamp DESC;

-- Connect to budget database  
SELECT * FROM budget.mt_events ORDER BY timestamp DESC;
```

## 🐛 Debugging

### View Event Flow in Aspire Dashboard

1. Open http://localhost:15000
2. Click **Traces** tab
3. Add transaction in UI
4. Watch trace showing:
   - Account.TransactionRecorded
   - TransactionEventHandler.HandleAsync
   - Budget.ExpenseRecorded

### Check Logs

In Aspire Dashboard:
- Click **Console** or **Structured Logs**
- Filter by service: `budgeteer-web`
- View application logs

## 📊 Understanding the Architecture

### Two Independent Domains

```
Account Domain (PostgreSQL #1)
├── Manages accounts
├── Records transactions
└── Events: AccountCreated, TransactionRecorded

Budget Domain (PostgreSQL #2)
├── Processes expenses/income
├── Categorizes spending
└── Events: ExpenseRecorded, IncomeRecorded
```

### Event Flow

```
User adds transaction
    ↓
AccountAggregate.RecordTransaction()
    ↓
TransactionRecorded event → accounts DB
    ↓
[In-process handler]
    ↓
TransactionEventHandler.HandleAsync()
    ↓
ExpenseRecorded event → budget DB
```

## ⚡ Quick Commands Reference

```powershell
# Run the app
cd Budgeteer.AppHost
dotnet run

# Build solution
dotnet build

# Clean solution
dotnet clean

# View solution in IDE
start Budgeteer.sln
```

## 🎯 What to Build Next

### Option 1: CSV Import (1 day)
- Add CSV upload to Transactions page
- Parse bank exports with CsvHelper
- Bulk create TransactionRecorded events

### Option 2: Expense Categorization (1 day)
- Create Categories page
- Add category assignment to Budget domain
- Show spending by category

### Option 3: Budget Planning (2 days)
- Monthly budget allocation
- Track spending vs budget
- Visual progress bars

## 🆘 Troubleshooting

### PostgreSQL containers not starting
```powershell
# Check Docker Desktop is running
docker ps

# Restart Docker Desktop
# Re-run: cd Budgeteer.AppHost && dotnet run
```

### Build errors
```powershell
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

### Can't access databases
- Verify Aspire Dashboard shows healthy containers
- Check connection strings in Aspire Dashboard
- Ensure no firewall blocking ports

## 📖 Learning Resources

- **Event Sourcing**: https://martinfowler.com/eaaDev/EventSourcing.html
- **Marten Docs**: https://martendb.io/events/
- **.NET Aspire**: https://learn.microsoft.com/dotnet/aspire/
- **DDD**: https://www.domainlanguage.com/ddd/

## 🎉 Success!

You now have a working event-sourced application with:
- ✅ Two independent event stores
- ✅ Domain-driven design
- ✅ Event flow between domains
- ✅ Full audit trail
- ✅ Aspire orchestration
- ✅ Production-ready patterns

**Next**: Add your first transaction and watch the events flow! 🚀
