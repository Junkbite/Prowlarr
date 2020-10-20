import { push } from 'connected-react-router';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { deleteMovie } from 'Store/Actions/movieActions';
import createIndexerSelector from 'Store/Selectors/createIndexerSelector';
import DeleteMovieModalContent from './DeleteMovieModalContent';

function createMapStateToProps() {
  return createSelector(
    createIndexerSelector(),
    (movie) => {
      return movie;
    }
  );
}

const mapDispatchToProps = {
  deleteMovie,
  push
};

class DeleteMovieModalContentConnector extends Component {

  //
  // Listeners

  onDeletePress = (deleteFiles, addImportExclusion) => {
    this.props.deleteMovie({
      id: this.props.movieId,
      deleteFiles,
      addImportExclusion
    });

    this.props.onModalClose(true);

    if (this.props.nextMovieRelativePath) {
      this.props.push(window.Prowlarr.urlBase + this.props.nextMovieRelativePath);
    }
  }

  //
  // Render

  render() {
    return (
      <DeleteMovieModalContent
        {...this.props}
        onDeletePress={this.onDeletePress}
      />
    );
  }
}

DeleteMovieModalContentConnector.propTypes = {
  movieId: PropTypes.number.isRequired,
  onModalClose: PropTypes.func.isRequired,
  deleteMovie: PropTypes.func.isRequired,
  push: PropTypes.func.isRequired,
  nextMovieRelativePath: PropTypes.string
};

export default connect(createMapStateToProps, mapDispatchToProps)(DeleteMovieModalContentConnector);
