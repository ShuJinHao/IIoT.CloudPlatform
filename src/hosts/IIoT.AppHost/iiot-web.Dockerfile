ARG NODE_BASE_IMAGE=node:22-slim
ARG NGINX_BASE_IMAGE=nginx:1.27-alpine
FROM ${NODE_BASE_IMAGE} AS build
WORKDIR /app

COPY src/ui/iiot-web/package*.json ./
RUN --mount=type=cache,target=/root/.npm npm ci

COPY src/ui/iiot-web/ ./
ARG VITE_AICOPILOT_CHALLENGE_URL=
ENV VITE_AICOPILOT_CHALLENGE_URL=$VITE_AICOPILOT_CHALLENGE_URL
RUN npm run build

FROM ${NGINX_BASE_IMAGE} AS final

COPY src/hosts/IIoT.AppHost/iiot-web.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

RUN sed -i \
        -e 's#pid /var/run/nginx.pid;#pid /tmp/nginx.pid;#' \
        -e 's#pid /run/nginx.pid;#pid /tmp/nginx.pid;#' \
        /etc/nginx/nginx.conf \
    && sed -i '/^http {/a\    client_body_temp_path /tmp/nginx/client_temp;\n    proxy_temp_path /tmp/nginx/proxy_temp;\n    fastcgi_temp_path /tmp/nginx/fastcgi_temp;\n    uwsgi_temp_path /tmp/nginx/uwsgi_temp;\n    scgi_temp_path /tmp/nginx/scgi_temp;' /etc/nginx/nginx.conf \
    && mkdir -p /tmp/nginx/client_temp /tmp/nginx/proxy_temp /tmp/nginx/fastcgi_temp /tmp/nginx/uwsgi_temp /tmp/nginx/scgi_temp \
    && chown -R 101:101 /tmp/nginx /var/cache/nginx /var/log/nginx /etc/nginx/conf.d /usr/share/nginx/html

EXPOSE 8080
USER 101:101
