FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy project files for caching restore layer
COPY Imaginary.sln ./
COPY src/Imaginary.Core/Imaginary.Core.csproj src/Imaginary.Core/
COPY src/Imaginary.Web/Imaginary.Web.csproj src/Imaginary.Web/

# Restore Web and Core dependencies
RUN dotnet restore src/Imaginary.Web/Imaginary.Web.csproj

# Copy source code
COPY src/Imaginary.Core/ src/Imaginary.Core/
COPY src/Imaginary.Web/ src/Imaginary.Web/

# Build and publish
WORKDIR /app/src/Imaginary.Web
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Imaginary.Web.dll"]
