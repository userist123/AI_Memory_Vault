import { createVuetify as CreateVuetify } from 'vuetify';
import * as components from 'vuetify/components';
import * as directives from 'vuetify/directives';
import * as labsComponents from 'vuetify/labs/components';
import 'vuetify/styles';

export function createVuetify() {
  return CreateVuetify({
    directives,
    components: {
      ...components,
      ...labsComponents,
    },
    theme: {
      defaultTheme: 'system',
    },
  });
}
