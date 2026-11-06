/** @type {import('next').NextConfig} */
const config = {
  output: "standalone",
  experimental: {
    typedRoutes: true,
  },
  images: {
    remotePatterns: [
      {
        protocol: "https",
        hostname: "**.blob.core.windows.net",
      },
      // Azurite en desarrollo. El emulador sirve por http y en un host que cambia
      // (localhost, o la IP del equipo cuando se prueba desde el móvil), así que el
      // patrón se acota por puerto y por la ruta de la cuenta de desarrollo en vez
      // de por nombre de host. Sin esto, next/image responde 400 y no se ve nada.
      {
        protocol: "http",
        hostname: "**",
        port: "10000",
        pathname: "/devstoreaccount1/**",
      },
    ],
  },
};

export default config;
