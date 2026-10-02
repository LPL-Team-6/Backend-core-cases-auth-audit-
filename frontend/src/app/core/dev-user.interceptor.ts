import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { DevUserService } from './dev-user.service';

// Attaches the dev-only X-Dev-User header to every API call, from whichever persona is
// currently selected in the UI (see DevUserService). The real auth replacement for this is a
// production authentication handler on the backend, not anything in this interceptor.
export const devUserInterceptor: HttpInterceptorFn = (req, next) => {
  const devUser = inject(DevUserService).current();
  return next(req.clone({ setHeaders: { 'X-Dev-User': devUser.username } }));
};
