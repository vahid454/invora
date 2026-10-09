FROM node:24.21.0-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci
COPY . ./
RUN npm run build
FROM nginx:1.30.5-alpine
COPY deployment/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/invora-web/browser/ /usr/share/nginx/html/
EXPOSE 8080
