FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY ["SpotifyBlazor/Server/SpotifyBlazor.Server.csproj", "SpotifyBlazor/Server/"]
COPY ["SpotifyBlazor/Shared/SpotifyBlazor.Shared.csproj", "SpotifyBlazor/Shared/"]

RUN dotnet restore "SpotifyBlazor/Server/SpotifyBlazor.Server.csproj"

COPY . .

RUN dotnet publish "SpotifyBlazor/Server/SpotifyBlazor.Server.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# Copy HTTPS certificate into container
COPY SpotifyBlazor/Server/docker-https.pfx /https/docker-https.pfx

# Configure Kestrel to use HTTPS
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/https/docker-https.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=dockerpassword

# Bind to HTTPS + HTTP
ENV ASPNETCORE_URLS="https://+:7151;http://+:5133"

EXPOSE 5133
EXPOSE 7151

ENTRYPOINT ["dotnet", "SpotifyBlazor.Server.dll"]
