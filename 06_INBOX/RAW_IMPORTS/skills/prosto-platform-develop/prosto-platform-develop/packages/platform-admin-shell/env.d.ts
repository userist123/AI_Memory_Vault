/// <reference types="vite/client" />
/* eslint-disable @typescript-eslint/naming-convention */

interface ImportMetaEnv {
  readonly VITE_ADMIN_BFF_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

/* eslint-disable @typescript-eslint/no-empty-object-type, @typescript-eslint/no-explicit-any */
declare module '*.vue' {
  import type { DefineComponent } from 'vue';
  const component: DefineComponent<{}, {}, any>;
  export default component;
}
