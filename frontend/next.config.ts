import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Imagem de produção enxuta (só o necessário para rodar), usada pelo Dockerfile.
  output: "standalone",
};

export default nextConfig;
