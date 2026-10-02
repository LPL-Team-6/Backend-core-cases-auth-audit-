import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { AiReviewResponse } from '@caseauth/angular-client/src/models/ai-review-response';
import { AiRecommendation } from '@caseauth/angular-client/src/models/ai-recommendation';

const RECOMMENDATION_BADGE: Record<AiRecommendation, string> = {
  Approve: 'badge--success',
  Reject: 'badge--danger',
  Escalate: 'badge--warning',
};

@Component({
  selector: 'app-ai-review-panel',
  imports: [DatePipe],
  template: `
    <h2>AI review</h2>
    @if (sorted().length === 0) {
      <p>No AI review has run yet.</p>
    } @else {
      @let latest = sorted()[0];
      <article class="ai-review ai-review--latest">
        <header>
          <span class="badge" [class]="badge(latest.recommendation)">{{ latest.recommendation }}</span>
          <strong>v{{ latest.version }}</strong>
          <span class="ai-review__model">{{ latest.modelName }} ({{ latest.modelVersion }})</span>
        </header>
        <!-- Rationale is model output: interpolation only, never [innerHTML], so Angular's
             sanitizer is never bypassed regardless of what the model returns. -->
        <p>{{ latest.rationale }}</p>
      </article>

      @if (sorted().length > 1) {
        <details class="ai-review-history">
          <summary>Earlier reviews ({{ sorted().length - 1 }})</summary>
          <ul>
            @for (review of sorted().slice(1); track review.id) {
              <li>
                <span class="badge" [class]="badge(review.recommendation)">{{ review.recommendation }}</span>
                v{{ review.version }} - {{ review.createdAt | date: 'short' }}
              </li>
            }
          </ul>
        </details>
      }
    }
  `,
  styles: `
    .ai-review {
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      padding: 1rem;
    }

    .ai-review header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
    }

    .ai-review__model {
      color: var(--color-text-muted);
      font-size: 0.85rem;
    }

    .ai-review-history {
      margin-top: 0.5rem;
      color: var(--color-text-muted);
    }
  `,
})
export class AiReviewPanelComponent {
  readonly aiReviews = input.required<AiReviewResponse[]>();

  readonly sorted = computed(() => [...this.aiReviews()].sort((a, b) => (b.version ?? 0) - (a.version ?? 0)));

  badge(recommendation: AiRecommendation | undefined): string {
    return recommendation ? RECOMMENDATION_BADGE[recommendation] : 'badge--neutral';
  }
}
