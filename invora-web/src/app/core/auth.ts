import {inject, Injectable, signal} from '@angular/core';
import {HttpClient,HttpErrorResponse} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {Session,User} from './models';
@Injectable({providedIn:'root'})
export class AuthService {
  private http=inject(HttpClient); private refreshTask:Promise<boolean>|null=null;
  readonly user=signal<User|null>(null); token=''; private expires=0;
  can(code:string){return this.user()?.permissions.includes(code)??false;}
  private accept(s:Session){this.token=s.accessToken;this.expires=Date.parse(s.expiresAtUtc);this.user.set(s.user);}
  async login(login:string,password:string){this.accept(await firstValueFrom(this.http.post<Session>('/api/v1/auth/login',{login,password},{withCredentials:true})));}
  async setup(data:Record<string,string>,bootstrap:string){this.accept(await firstValueFrom(this.http.post<Session>('/api/v1/auth/register',data,{withCredentials:true,headers:{'X-Invora-Bootstrap':bootstrap}})));}
  refresh():Promise<boolean>{if(this.refreshTask)return this.refreshTask;this.refreshTask=firstValueFrom(this.http.post<Session>('/api/v1/auth/refresh',{}, {withCredentials:true,headers:{'X-Invora-CSRF':'1'}})).then(s=>{this.accept(s);return true;}).catch(e=>{if(e instanceof HttpErrorResponse && (e.status===401||e.status===403)){this.clear();return false;}throw e;}).finally(()=>this.refreshTask=null);return this.refreshTask;}
  async ready(){if(this.token && this.expires-Date.now()>30000)return true;return this.refresh();}
  async logout(){try{await firstValueFrom(this.http.post('/api/v1/auth/logout',{}, {withCredentials:true,headers:{'X-Invora-CSRF':'1'}}));}finally{this.clear();}}
  clear(){this.token='';this.expires=0;this.user.set(null);}
}
