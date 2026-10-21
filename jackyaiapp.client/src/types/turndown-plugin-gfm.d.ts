/** The package ships no types; these are the plugins it exports (each a TurndownService plugin). */
declare module '@joplin/turndown-plugin-gfm' {
  import type TurndownService from 'turndown';

  type Plugin = (service: TurndownService) => void;

  export const gfm: Plugin;
  export const tables: Plugin;
  export const strikethrough: Plugin;
  export const taskListItems: Plugin;
  export const highlightedCodeBlock: Plugin;
}
