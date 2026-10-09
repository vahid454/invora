import {inject,Injectable,signal} from '@angular/core';
import {HttpClient,HttpErrorResponse,HttpInterceptorFn} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {AuthService} from './auth';
import {Branch} from './models';
export const authInterceptor:HttpInterceptorFn=(req,next)=>{const auth=inject(AuthService);return next(auth.token && !req.url.endsWith('/auth/refresh') && !req.url.endsWith('/auth/login')?req.clone({setHeaders:{Authorization:'Bearer '+auth.token}}):req);};
@Injectable({providedIn:'root'})
export class ApiService {
  private http=inject(HttpClient);private auth=inject(AuthService);private keys=new Map<string,string>();
  async get<T>(path:string):Promise<T>{if(!await this.auth.ready())throw new Error('Your session expired. Please sign in again.');return firstValueFrom(this.http.get<T>('/api/v1'+path));}
  async mutate<T>(path:string,input:unknown,method:'POST'|'PUT'='POST'):Promise<T>{if(!await this.auth.ready())throw new Error('Your session expired. Please sign in again.');const signature=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(method+path+JSON.stringify(input))))).map(b=>b.toString(16).padStart(2,'0')).join('');const storageKey='invora.request.'+signature;const key=this.keys.get(signature)??sessionStorage.getItem(storageKey)??crypto.randomUUID();this.keys.set(signature,key);sessionStorage.setItem(storageKey,key);try{const result=await firstValueFrom(this.http.request<T>(method,'/api/v1'+path,{body:input,headers:{'Idempotency-Key':key}}));this.keys.delete(signature);sessionStorage.removeItem(storageKey);return result;}catch(e){if(e instanceof HttpErrorResponse && e.status>=400&&e.status<500&&e.status!==408){this.keys.delete(signature);sessionStorage.removeItem(storageKey);}throw e;}}
  async blob(path:string){if(!await this.auth.ready())throw new Error('Please sign in.');return firstValueFrom(this.http.get('/api/v1'+path,{responseType:'blob'}));}
  saveBlob(blob:Blob,name:string){const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=name;link.click();setTimeout(()=>URL.revokeObjectURL(url),60000);}
  async download(path:string,name:string){if(!await this.auth.ready())throw new Error('Please sign in.');const blob=await firstValueFrom(this.http.get('/api/v1'+path,{responseType:'blob'}));const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=name;link.click();setTimeout(()=>URL.revokeObjectURL(url),60000);}
}
@Injectable({providedIn:'root'})
export class BranchContext {
  private api=inject(ApiService);readonly branches=signal<Branch[]>([]);readonly id=signal('');
  async load(){const branches=await this.api.get<Branch[]>('/branches');this.branches.set(branches);const saved=localStorage.getItem('invora.branch');const current=this.id();this.id.set(branches.find(x=>x.id===current)?.id??branches.find(x=>x.id===saved)?.id??branches[0]?.id??'');}
  select(id:string){if(this.branches().some(x=>x.id===id)){this.id.set(id);localStorage.setItem('invora.branch',id);}}
  query(){if(!this.id())throw new Error('Choose an authorized branch first.');return 'branchId='+encodeURIComponent(this.id());}
}
export function errorText(e:unknown):string {if(e instanceof HttpErrorResponse){if(e.status===0)return 'Connection lost. The operation may have completed. Retry will use the same request key.';const errors=e.error?.errors;if(errors && typeof errors==='object'){const messages=Object.values(errors).flatMap(value=>Array.isArray(value)?value.filter((message):message is string=>typeof message==='string'):typeof value==='string'?[value]:[]);if(messages.length)return messages.join(' ');}return e.error?.detail??e.error?.title??('Request failed ('+e.status+').');}return e instanceof Error?e.message:'Unable to complete this action.';}
