export class ScanQueue {
  private pending:Promise<void>=Promise.resolve(); private accepted=new Set<string>();
  constructor(private handle:(value:string)=>Promise<void>,private report:(message:string)=>void,private deduplicate=true){}
  add(raw:string){const value=raw.trim();if(!value)return;if(this.deduplicate&&this.accepted.has(value)){this.report('Already scanned in this batch.');return;}this.accepted.add(value);this.pending=this.pending.then(()=>this.handle(value)).catch(e=>{this.accepted.delete(value);this.report(e instanceof Error?e.message:'Scan failed.');});}
  reset(){this.accepted.clear();}idle(){return this.pending;}
}
