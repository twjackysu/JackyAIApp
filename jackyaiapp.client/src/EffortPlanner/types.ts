export interface Task {
  id: number;
  name?: string;
  label: string;
  description?: string;
  labels?: string[];
  days: number;
  /**
   * taskCardKey
   */
  key: string;
}

export interface TaskCardProp {
  id: number;
  label: string;
  description?: string;
  days: number;
  color: string;
  key: string;
}
