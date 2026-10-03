import { Component, computed, input } from '@angular/core';

import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';
import { findingToTodo, TodoItem } from '../../../core/finding-todo';

// R2: plain-language "what I need from my client" list. R14: FYI items collapse below the
// open Blocking ones so the advisor sees what actually blocks the account first.
@Component({
  selector: 'app-advisor-todo-list',
  template: `
    <h2>What I need from my client</h2>
    @if (blocking().length === 0 && fyi().length === 0) {
      <p>Nothing outstanding - no findings on this case.</p>
    } @else {
      @if (blocking().length === 0) {
        <p>No blocking issues - this case is ready to move forward.</p>
      } @else {
        <ul class="todo-list">
          @for (todo of blocking(); track todo.findingId) {
            <li class="todo-list__item todo-list__item--blocking">
              <span class="badge badge--danger"><span aria-hidden="true">⛔</span> Blocking</span>
              {{ todo.sentence }}
            </li>
          }
        </ul>
      }

      @if (fyi().length > 0) {
        <details class="todo-list__fyi">
          <summary>For your information ({{ fyi().length }})</summary>
          <ul class="todo-list">
            @for (todo of fyi(); track todo.findingId) {
              <li class="todo-list__item todo-list__item--fyi">
                <span class="badge badge--neutral"><span aria-hidden="true">ℹ</span> FYI</span>
                {{ todo.sentence }}
              </li>
            }
          </ul>
        </details>
      }
    }
  `,
  styles: `
    .todo-list {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .todo-list__item {
      display: flex;
      align-items: flex-start;
      gap: 0.6rem;
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      padding: 0.6rem 0.9rem;
    }

    .todo-list__item--blocking {
      border-color: var(--color-danger);
    }

    .todo-list__fyi {
      margin-top: 0.75rem;
      color: var(--color-text-muted);
    }
  `,
})
export class AdvisorTodoListComponent {
  readonly findings = input.required<FindingResponse[]>();

  private readonly todos = computed<TodoItem[]>(() => this.findings().map(findingToTodo));
  readonly blocking = computed(() => this.todos().filter((t) => t.tag === 'Blocking'));
  readonly fyi = computed(() => this.todos().filter((t) => t.tag === 'FYI'));
}
