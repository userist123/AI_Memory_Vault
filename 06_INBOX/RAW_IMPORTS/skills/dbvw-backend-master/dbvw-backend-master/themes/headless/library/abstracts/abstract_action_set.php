<?php

/**
 * 
 * A baseline class for encapsulating a set of Wordpress actions
 * with related, or even inter-related functionality and dependency.
 */
abstract class WS_Action_Set {

	/**
	 * @var array(string => string) the set of actions => callbacks
	 * that this actionset initializes and defines. @override child.
	 *
	 */
	protected $actions = array();

	/**
	 * Initializes the action set with the supplied callbacks.
	 */
	public function __construct( $actions ) {
		foreach ( $actions as $filter => $cbdata ) {
			if ( is_array($cbdata ) ) {
				add_action( $filter, array( $this, $cbdata[0] ), $cbdata[1], $cbdata[2] );
			} else {
				add_action( $filter, array( $this, $cbdata ) );
			}
		}
	}
}

?>
