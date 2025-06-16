This folder can only place files related to Entity Framework, if you are not familiar with these please do not change here
===
Install the dotnet SDK, then run the following command in CMD
---
1. dotnet tool install --global dotnet-ef --version 8.0.17
2. dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.17
3. dotnet ef migrations add InitialCreate -o Data/Migrations --context AzureSQLDBContext
4. dotnet ef database update --context AzureSQLDBContext
