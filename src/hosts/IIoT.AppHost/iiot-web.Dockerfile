FROM node:22-slim AS build
WORKDIR /src/ui/iiot-web

COPY src/ui/iiot-web/package*.json ./
RUN --mount=type=cache,target=/root/.npm npm ci

COPY src/ui/iiot-web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS final

COPY src/hosts/IIoT.AppHost/iiot-web.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/ui/iiot-web/dist /usr/share/nginx/html

EXPOSE 80

