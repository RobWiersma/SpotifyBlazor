FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY ["SpotifyBlazor/Client/SpotifyBlazor.Client.csproj", "SpotifyBlazor/Client/"]
COPY ["SpotifyBlazor/Shared/SpotifyBlazor.Shared.csproj", "SpotifyBlazor/Shared/"]

RUN dotnet restore "SpotifyBlazor/Client/SpotifyBlazor.Client.csproj"

COPY . .

RUN dotnet publish "SpotifyBlazor/Client/SpotifyBlazor.Client.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

COPY --from=build /app/publish/wwwroot .

# Override Nginx default port
EXPOSE 7151

# Replace default Nginx config to listen on 7151
RUN printf "server { listen 7151; root /usr/share/nginx/html; index index.html; }" > /etc/nginx/conf.d/default.conf
