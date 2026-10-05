interface Props {
  onStart: () => void;
}

/**
 * The resting state of the column: what the building sees when nobody is
 * standing at it. Carries no visitor data of any kind.
 */
export function AttractScreen({ onStart }: Props) {
  return (
    <button className="attract" onClick={onStart}>
      <h1>Welkom</h1>
      <p>Raak het scherm aan om uw vergaderzaal te vinden</p>
    </button>
  );
}
