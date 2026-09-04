<?php

add_action('acf/save_post', 'populate_about_content', 20);

add_action('save_post', 'populate_the_content');

function populate_the_content($post_id) {
	$ptype = get_post_type( $post_id );
	if( $ptype != 'acf-field-group' || $ptype != 'attachment'){

	    //Check it's not an auto save routine
		if ( defined('DOING_AUTOSAVE') && DOING_AUTOSAVE ) 
			return;

	    //Perform permission checks! For example:
		if ( !current_user_can('edit_post', $post_id) ) 
			return;

	    //Check your nonce!

	    //If calling wp_update_post, unhook this function so it doesn't loop infinitely
		remove_action('save_post', 'populate_the_content');

		$fields = array();
		$post_content = ' ';

		switch ( $ptype ){
			case 'projects':
				$fields = array('location', 'client', 'services', 'timeline', 'short_description','search_description' );
				if( have_rows('stories', $post_id) ):
					while ( have_rows('stories', $post_id) ) : the_row();
						$item = get_sub_field('story_text');
						$post_content .= $item  . ' ';
					endwhile;
				endif;
				break;
			case 'people':
				$fields = array('job_title', 'bio');
				break;
			case 'news':
				$fields = array('description');
				if( have_rows('stories', $post_id) ):
					while ( have_rows('stories', $post_id) ) : the_row();
						$item = get_sub_field('story_text');
						$post_content .= $item  . ' ';
					endwhile;
				endif;			
				break;	
			case 'about':
				$fields = array('search_description');	
				break;	
			case 'jobs':
				$fields = array('job_description', 'how_to_apply');	
				break;										

			default:
			break;	
		}

		if( count($fields) > 0 ){
			for ( $i = 0; $i < count($fields); $i++ ) { 
				$post_content .= get_field( $fields[$i], $post_id ) . ' ';
			}	
			// call wp_update_post update, which calls save_post again. E.g:
			wp_update_post(array('ID' => $post_id, 'post_content' => $post_content));	
		}

	    // re-hook this function
		add_action('save_post', 'populate_the_content');

	}
}


function populate_about_content() {

	$screen = get_current_screen();

	$screen = $screen->id;
	write_log($screen);
	$about_screen = 'toplevel_page_about-page';
	$about_page_id = 1305;

	// if( $screen == $about_screen ){
	// 	write_log('saving about screen');
	// }

	$fields = array('search_description','short_introduction','longer_introduction','people_statement','services_statement','clients_statement');

	$post_content = ' ';

	if( count($fields) > 0 ){
		
		for ( $i = 0; $i < count($fields); $i++ ) { 
			$post_content .= get_field( $fields[$i], 'option' ) . ' ';
		}

		if( have_rows('services', 'option') ):
			while ( have_rows('services', 'option') ) : the_row();
		if( have_rows('sub_services', 'option') ):
			while ( have_rows('sub_services', 'option') ) : the_row();
		$item = get_sub_field('sub_service_title');
		$post_content .= $item  . ' ';
		endwhile;
		endif;
		endwhile;
		endif;

		if( have_rows('awards', 'option') ):
			while ( have_rows('awards', 'option') ) : the_row();
		$item = get_sub_field('award_title');
		$post_content .= $item  . ' ';
		endwhile;
		endif;		

		wp_update_post(array('ID' => 1305, 'post_content' => $post_content));	
	}

}


function write_log ( $log )  {
	if ( is_array( $log ) || is_object( $log ) ) {
		error_log( print_r( $log, true ) );
	} else {
		error_log( $log );
	}
}


?>