import js from '@eslint/js';
import vuePrettierConfig from '@vue/eslint-config-prettier';
import {
  defineConfigWithVueTs,
  vueTsConfigs,
} from '@vue/eslint-config-typescript';
import boundaries from 'eslint-plugin-boundaries';
import pluginVue from 'eslint-plugin-vue';
import globals from 'globals';
import tsEslint from 'typescript-eslint';

export default tsEslint.config(
  {
    name: 'app/files-to-ignore',
    ignores: [
      'public/**',
      '**/dist/**',
      '**/dist-ssr/**',
      '**/coverage/**',
      '**/node_modules/**',
      '**/.idea/**',
    ],
  },
  {
    name: 'app/js-files',
    files: ['**/*.{js,mjs,cjs}'],
    extends: [js.configs.recommended],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'module',
      globals: {
        ...globals.node,
      },
    },
  },
  {
    name: 'app/ts-vue-files',
    files: ['**/*.{ts,tsx,mts,cts,vue}'],
    extends: [
      js.configs.recommended,
      ...tsEslint.configs.strict,
      ...tsEslint.configs.stylistic,
    ],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'module',
      globals: {
        ...globals.node,
      },
    },
  },
  ...defineConfigWithVueTs(
    pluginVue.configs['flat/recommended'],
    vueTsConfigs.recommended,
    vuePrettierConfig,
  ),
  {
    name: 'app/override-rules',
    rules: {
      '@typescript-eslint/consistent-type-imports': [
        'warn',
        { disallowTypeAnnotations: false },
      ],
      '@typescript-eslint/naming-convention': [
        'error',
        { selector: 'interface', format: ['PascalCase'], prefix: ['I'] },
        {
          selector: 'typeAlias',
          format: ['PascalCase'],
          suffix: ['Type'],
          leadingUnderscore: 'allowSingleOrDouble',
        },
        { selector: 'enum', format: ['PascalCase'] },
        { selector: 'enumMember', format: ['PascalCase'] },
      ],
      '@typescript-eslint/no-extraneous-class': 'off',
      // '@typescript-eslint/no-inferrable-types': 'warn',
      '@typescript-eslint/no-non-null-assertion': 'warn',
      '@typescript-eslint/no-shadow': 'error',
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
        },
      ],
      curly: ['warn', 'multi-line', 'consistent'],
      'no-shadow': 'off', // See: https://typescript-eslint.io/rules/no-shadow/#how-to-use
      'no-unused-vars': 'off',
      'prefer-rest-params': 'warn',
      'spaced-comment': [
        'warn',
        'always',
        {
          //-+-+-+-+-+-+-+-+
          // Banner example
          //-+-+-+-+-+-+-+-+

          //----------------
          // Banner example
          //----------------
          line: {
            markers: ['/'],
            exceptions: ['-', '-+'],
          },

          /*****************
           * Banner example
           *****************/
          block: {
            markers: ['!'],
            exceptions: ['*'],
            balanced: true,
          },
        },
      ],
    },
  },
  {
    name: 'platform-admin-shell/boundaries',
    files: ['packages/platform-admin-shell/src/**/*.{ts,vue}'],
    plugins: {
      boundaries,
    },
    settings: {
      'boundaries/elements': [
        {
          type: 'shared',
          pattern: 'packages/platform-admin-shell/src/shared/**/*',
        },
        {
          type: 'entities',
          pattern: 'packages/platform-admin-shell/src/entities/**/*',
        },
        {
          type: 'features',
          pattern: 'packages/platform-admin-shell/src/features/**/*',
        },
        {
          type: 'processes',
          pattern: 'packages/platform-admin-shell/src/processes/**/*',
        },
        {
          type: 'widgets',
          pattern: 'packages/platform-admin-shell/src/widgets/**/*',
        },
        {
          type: 'pages',
          pattern: 'packages/platform-admin-shell/src/pages/**/*',
        },
        { type: 'app', pattern: 'packages/platform-admin-shell/src/app/**/*' },
      ],
      'boundaries/ignore': [
        '**/node_modules/**',
        '**/dist/**',
        '**/coverage/**',
      ],
      'import/resolver': {
        typescript: {
          project: 'packages/platform-admin-shell/tsconfig.json',
        },
      },
    },
    rules: {
      'boundaries/dependencies': [
        'error',
        {
          default: 'disallow',
          checkInternals: true,
          policies: [
            {
              from: { element: { type: 'entities' } },
              allow: {
                to: { element: { types: { anyOf: ['shared', 'entities'] } } },
              },
            },
            {
              from: { element: { type: 'features' } },
              allow: {
                to: {
                  element: {
                    types: { anyOf: ['entities', 'shared', 'features'] },
                  },
                },
              },
            },
            {
              from: { element: { type: 'processes' } },
              allow: {
                to: {
                  element: {
                    types: {
                      anyOf: ['features', 'entities', 'shared', 'processes'],
                    },
                  },
                },
              },
            },
            {
              from: { element: { type: 'widgets' } },
              allow: {
                to: {
                  element: {
                    types: {
                      anyOf: ['features', 'entities', 'shared', 'widgets'],
                    },
                  },
                },
              },
            },
            {
              from: { element: { type: 'pages' } },
              allow: {
                to: {
                  element: {
                    types: {
                      anyOf: [
                        'widgets',
                        'features',
                        'entities',
                        'shared',
                        'pages',
                      ],
                    },
                  },
                },
              },
            },
            {
              from: { element: { type: 'app' } },
              allow: {
                to: {
                  element: {
                    types: {
                      anyOf: [
                        'pages',
                        'widgets',
                        'processes',
                        'features',
                        'entities',
                        'shared',
                        'app',
                      ],
                    },
                  },
                },
              },
            },
            {
              from: { element: { type: 'shared' } },
              allow: {
                to: { element: { types: { anyOf: ['shared'] } } },
              },
            },
            {
              from: {
                element: { type: 'features', fileInternalPath: '**/model/**' },
              },
              disallow: [
                { module: { origin: 'external', source: 'vue' } },
                { module: { origin: 'external', source: 'pinia' } },
                { module: { origin: 'external', source: 'vuetify' } },
              ],
            },
            {
              from: {
                element: { type: 'shared', fileInternalPath: '**/api/**' },
              },
              disallow: [
                { module: { origin: 'external', source: 'vue' } },
                { module: { origin: 'external', source: 'pinia' } },
                { module: { origin: 'external', source: 'vuetify' } },
              ],
            },
            {
              from: {
                element: { type: 'entities', fileInternalPath: '**/model/**' },
              },
              disallow: [
                { module: { origin: 'external', source: 'vue' } },
                { module: { origin: 'external', source: 'pinia' } },
                { module: { origin: 'external', source: 'vuetify' } },
              ],
            },
          ],
        },
      ],
    },
  },
);
