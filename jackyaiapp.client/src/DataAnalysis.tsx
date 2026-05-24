// Entry-point shim — keeps the existing import path `./DataAnalysis` working
// while the real implementation lives in the DataAnalysis/ folder.
export { default } from './DataAnalysis/index';
