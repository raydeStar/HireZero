import {Component,type ReactNode} from 'react';
import {CircleAlert} from 'lucide-react';

/** Keeps one broken view from blanking the whole workspace. */
export class ViewBoundary extends Component<{view:string;children:ReactNode},{error:Error|null}>{
  state={error:null as Error|null};
  static getDerivedStateFromError(error:Error){return {error};}
  componentDidUpdate(previous:{view:string}){if(previous.view!==this.props.view&&this.state.error)this.setState({error:null});}
  render(){
    if(!this.state.error)return this.props.children;
    return <div className="fe-page"><div className="fe-empty" role="alert"><CircleAlert size={34}/><h3>This view hit a problem</h3>
      <p>Your work is saved. Try the view again, or pick another one from the menu.</p>
      <details><summary>Details</summary><code>{this.state.error.message}</code></details>
      <button type="button" className="primary" onClick={()=>this.setState({error:null})}>Try again</button></div></div>;
  }
}
