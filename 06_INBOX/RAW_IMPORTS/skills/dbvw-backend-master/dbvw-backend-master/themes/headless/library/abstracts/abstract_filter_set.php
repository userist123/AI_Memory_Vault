<?php

/**
 * A baseline class for encapsulating a set of Wordpress filters
 * with related, or even inter-related functionality and dependency.
 */
abstract class WS_Filter_Set {

	/**
	 * @var array( string => array(string, int, int) )
	 * an array of filter names mapped to callback functions, paired with priority and argument counts
	 *
	 */

	/** 
   	 * Initialize the set of filters with the supplied callback functions and priorities.
   	 */
	public function __construct( $filters ) {
		foreach ( $filters as $filter => $cbdata ) {
			if ( is_array($cbdata ) ) {
				add_filter( $filter, array( $this, $cbdata[0] ), $cbdata[1], $cbdata[2] );
			} else {
				add_filter( $filter, array( $this, $cbdata ) );
			}
		}
	}

}

?>