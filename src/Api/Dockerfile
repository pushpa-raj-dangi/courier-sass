FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Api/Api.csproj src/Api/
COPY src/SharedKernel/SharedKernel.csproj src/SharedKernel/
COPY src/Persistence/Persistence.csproj src/Persistence/
COPY src/Modules/Identity/Identity.csproj src/Modules/Identity/
COPY src/Modules/Audit/Audit.csproj src/Modules/Audit/
COPY src/Modules/Branches/Branches.csproj src/Modules/Branches/
COPY src/Modules/Geography/Geography.csproj src/Modules/Geography/
COPY src/Modules/Merchants/Merchants.csproj src/Modules/Merchants/
RUN dotnet restore src/Api/Api.csproj
COPY src/ src/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
